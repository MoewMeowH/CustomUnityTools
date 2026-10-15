#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HB.Cheat.UI;
using UnityEngine;

namespace HB.Cheat
{
    /// <summary>
    /// 치트 실행·히스토리·UI 호스트 관리. (InGameConsole처럼 정적 싱글톤)
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [AutoStaticsCleanup]
#endif
    public static partial class CheatSystem
    {
        private const string FavoritesPrefsKey = "HB_Cheat.Favorites";

        private static bool s_IsInitialized;
        private static int s_NextExecutionId = 1;
        private static CheatRunner s_Runner;
        private static ICheatPanelHost s_ActiveHost;
        private static readonly List<CheatExecution> s_History = new List<CheatExecution>();
        // 서버 응답에 요청 식별자가 없으므로 한 번에 하나만 보내고 나머지는 순서대로 대기시킨다
        private static readonly Queue<CheatExecution> s_ServerQueue = new Queue<CheatExecution>();
        private static CheatExecution s_InFlight;
        // 시간 초과(또는 응답 대기 중 종료) 뒤에는 늦은 응답이 다음 요청에 붙지 않도록 큐를 멈춘다.
        // 멈춘 동안 들어온 응답 한 건은 버리고 재개하며, 끝내 안 오면 ResumeServerQueue로 재개한다.
        private static bool s_ServerQueuePaused;
        private static string s_ServerQueuePauseReason;
        private static bool s_IsSendingNext;
        private static bool s_IsInitializing;
        private static bool s_IsShuttingDown;
        private static int s_MainThreadId = -1;
        // 메인 스레드가 아닌 곳에서 온 응답은 여기에 쌓아 두고 Tick에서 처리한다
        private static readonly ConcurrentQueue<(bool Success, string Message)> s_PendingReports = new ConcurrentQueue<(bool, string)>();
        private static readonly Dictionary<string, object[]> s_LastValues = new Dictionary<string, object[]>(StringComparer.Ordinal);
        private static readonly List<ICheatBridge> s_Bridges = new List<ICheatBridge>();
        private static HashSet<string> s_Favorites;

        /// <summary>생성 레지스트리가 SubsystemRegistration에서 채운다.</summary>
        public static CheatRuntimeOptions Options
        {
            get => CheatBootstrapState.Options;
            set => CheatBootstrapState.Options = value ?? new CheatRuntimeOptions();
        }

        /// <summary>서버 치트 전송 구현. 없으면 서버 치트는 Failed.</summary>
        public static ICheatServerTransport ServerTransport { get; set; }

        /// <summary>InGameConsole 연동이 SubsystemRegistration에서 넣는다.</summary>
        public static ICheatPanelHost ConsoleHost
        {
            get => CheatBootstrapState.ConsoleHost;
            set => CheatBootstrapState.ConsoleHost = value;
        }

        public static bool IsInitialized => s_IsInitialized;

        /// <summary>패널이 콘솔 탭으로 붙었는지</summary>
        public static bool IsHostedInConsole => s_ActiveHost != null && s_ActiveHost == ConsoleHost;

        /// <summary>실행이 추가되거나 상태가 바뀔 때</summary>
        public static event Action<CheatExecution> ExecutionChanged;

        public static event Action FavoritesChanged;

        /// <summary>서버 큐가 멈추거나 재개될 때</summary>
        public static event Action ServerQueueChanged;

        /// <summary>시간 초과 등으로 서버 치트 전송이 멈춰 있는지</summary>
        public static bool IsServerQueuePaused => s_ServerQueuePaused;

        /// <summary>멈춘 이유 (멈추지 않았으면 null)</summary>
        public static string ServerQueuePauseReason => s_ServerQueuePaused ? s_ServerQueuePauseReason : null;

        /// <summary>전송을 기다리는 서버 치트 수 (전송 중인 건 제외)</summary>
        public static int QueuedServerCount => s_ServerQueue.Count;

        /// <summary>최근 실행 (오래된 것부터)</summary>
        public static IReadOnlyList<CheatExecution> History => s_History;

#if !(UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER)
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnDomainReload()
        {
            s_IsInitialized = false;
            s_NextExecutionId = 1;
            s_Runner = null;
            s_ActiveHost = null;
            s_History.Clear();
            s_ServerQueue.Clear();
            s_InFlight = null;
            s_ServerQueuePaused = false;
            s_ServerQueuePauseReason = null;
            s_IsSendingNext = false;
            s_IsInitializing = false;
            s_IsShuttingDown = false;
            s_MainThreadId = -1;
            while (s_PendingReports.TryDequeue(out _)) { }
            s_LastValues.Clear();
            s_Bridges.Clear();
            s_Favorites = null;
            ServerTransport = null;
            ExecutionChanged = null;
            FavoritesChanged = null;
            ServerQueueChanged = null;
            CheatOptionCache.Clear();
        }
#endif
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitializeOnGameStart()
        {
            if (Options != null && Options.InitMode == CheatInitMode.AutoOnGameStart)
                Initialize();
        }

        public static void Initialize()
        {
            if (s_IsInitialized || s_IsInitializing) return;
            if (s_IsShuttingDown)
            {
                // 종료 정리 중(실패 통지 구독자 등)에 다시 만들면 정리가 새 러너까지 지운다
                Debug.LogWarning("[HB_Cheat] Initialize ignored: the cheat system is shutting down.");
                return;
            }

            s_IsInitializing = true;
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            GameObject go = null;
            try
            {
                go = new GameObject("[HB_Cheat]");
                if (Options.DontDestroyOnLoad)
                    UnityEngine.Object.DontDestroyOnLoad(go);
                s_Runner = go.AddComponent<CheatRunner>();

                // 콘솔이 있으면 탭으로, 없거나 초기화 안 됐으면 독립 패널
                var panel = s_Runner.Panel;
                if (Options.PreferConsoleTab && ConsoleHost != null && ConsoleHost.TryAttach(panel))
                    s_ActiveHost = ConsoleHost;
                else
                    s_ActiveHost = s_Runner.CreateStandaloneHost();

                s_IsInitialized = true;
            }
            catch
            {
                // 반쯤 만든 상태로 남기지 않는다 → 원인을 고친 뒤 다시 Initialize 할 수 있다
                s_Runner = null;
                s_ActiveHost = null;
                if (go != null)
                {
                    // 편집 모드에서는 Destroy가 동작하지 않아 씬에 남는다
                    if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                    else UnityEngine.Object.DestroyImmediate(go);
                }
                throw;
            }
            finally
            {
                s_IsInitializing = false;
            }

            for (int i = 0; i < s_Bridges.Count; i++)
                StartBridge(s_Bridges[i]);
        }

        /// <summary>
        /// 러너 파괴 시 호출된다. 대기 중인 서버 치트는 실패로 끝내고, 콘솔 탭에서 패널을 뗀다.
        /// 응답을 기다리던 건이 있었으면 큐를 멈춘 채로 둔다 (그 응답이 다음 초기화 뒤의 요청에 붙지 않도록).
        /// 현재 러너가 아닌 러너(초기화 실패로 지운 러너 등)의 파괴는 무시한다. Destroy는 프레임 끝에 일어나므로
        /// 그 사이 새로 초기화했다면 새 세션을 끄면 안 된다.
        /// </summary>
        internal static void Shutdown(CheatRunner runner)
        {
            // 파괴 중인 러너는 Unity의 == 기준으로 null처럼 보이므로 참조로 비교한다
            if (!ReferenceEquals(runner, s_Runner)) return;
            if (!s_IsInitialized || s_IsShuttingDown) return;
            s_IsShuttingDown = true;
            s_IsInitialized = false;

            var host = s_ActiveHost;
            try
            {
                for (int i = 0; i < s_Bridges.Count; i++)
                {
                    try { s_Bridges[i].Stop(); }
                    catch (Exception e) { Debug.LogException(e); }
                }

                // 이미 도착한 응답은 먼저 정산한다 (버리면 응답이 왔는데도 미응답으로 보고 멈춘 채 남는다).
                // s_IsInitialized가 false라 이 과정에서 다음 건을 보내지는 않는다.
                while (s_PendingReports.TryDequeue(out var report))
                    HandleServerResult(report.Success, report.Message);

                if (s_InFlight != null)
                {
                    var inFlight = s_InFlight;
                    s_InFlight = null;
                    PauseServerQueue("The previous session was waiting for a server response");
                    Finish(inFlight, CheatStatus.Failed, "Cheat system shut down", notify: true);
                }
                while (s_ServerQueue.Count > 0)
                    Finish(s_ServerQueue.Dequeue(), CheatStatus.Failed, "Cheat system shut down", notify: true);

                try { host?.Detach(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                s_Runner = null;
                s_ActiveHost = null;
                s_IsShuttingDown = false;
            }
        }

        #region Panel

        public static bool IsOpen => s_ActiveHost != null && s_ActiveHost.IsOpen;

        public static void Open() => s_ActiveHost?.Open();
        public static void Close() => s_ActiveHost?.Close();

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>패널을 열고 해당 치트를 선택한다 (인자 값을 넘기면 그 값으로 채운다).</summary>
        public static bool ShowCheat(string cheatId, object[] values = null)
        {
            if (s_Runner == null || !CheatRegistry.TryGet(cheatId, out var definition)) return false;
            s_Runner.Panel.Show(definition, values);
            Open();
            return true;
        }

        #endregion

        #region Bridges

        public static void RegisterBridge(ICheatBridge bridge)
        {
            if (bridge == null || s_Bridges.Contains(bridge)) return;
            s_Bridges.Add(bridge);
            if (s_IsInitialized) StartBridge(bridge);
        }

        private static void StartBridge(ICheatBridge bridge)
        {
            try { bridge.Start(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        #endregion

        #region Execute

        /// <summary>
        /// 인자 검증. 범위, 목록 포함 여부, null을 확인한다.
        /// </summary>
        public static bool Validate(CheatDefinition definition, object[] values, out string error)
        {
            if (definition == null)
            {
                error = "Cheat definition is null";
                return false;
            }
            if (values == null || values.Length != definition.Args.Length)
            {
                error = $"'{definition.Id}' expects {definition.Args.Length} argument(s)";
                return false;
            }

            for (int i = 0; i < definition.Args.Length; i++)
            {
                var arg = definition.Args[i];
                object value = values[i];

                if (value == null)
                {
                    error = $"'{arg.DisplayLabel}' is not selected";
                    return false;
                }

                if (arg.HasRange && arg.Editor == CheatEditorKind.Number)
                {
                    double d = CheatValueParser.ToDouble(value);
                    if (d < arg.Min || d > arg.Max)
                    {
                        error = $"'{arg.DisplayLabel}' must be in [{arg.Min}, {arg.Max}]";
                        return false;
                    }
                }

                if (arg.HasOptions)
                {
                    var options = CheatOptionCache.Get(definition, i, values, out string optionError);
                    if (optionError != null)
                    {
                        error = optionError;
                        return false;
                    }
                    if (!ContainsValue(options, value))
                    {
                        error = $"'{arg.DisplayLabel}': {CheatValueParser.ToText(value)} is not in the option list";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        public static CheatExecution Execute(CheatDefinition definition, object[] values)
        {
            var copy = values != null ? (object[])values.Clone() : Array.Empty<object>();
            var execution = new CheatExecution(s_NextExecutionId++, definition, copy, Time.realtimeSinceStartup);

            if (definition != null)
                s_LastValues[definition.Id] = copy;

            if (!Validate(definition, copy, out string error))
            {
                Finish(execution, CheatStatus.Failed, error, notify: false);
                AddHistory(execution);
                return execution;
            }

            if (definition.Target == CheatTarget.Local)
            {
                RunLocal(execution);
                AddHistory(execution);
                return execution;
            }

            string command;
            try
            {
                command = definition.ServerCommandBuilder?.Invoke(new CheatArgs(copy));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(execution, CheatStatus.Failed, e.Message, notify: false);
                AddHistory(execution);
                return execution;
            }

            execution.Command = command;
            SendToServer(execution);
            return execution;
        }

        /// <summary>조합 없이 문자열 그대로 서버에 보낸다.</summary>
        public static CheatExecution ExecuteRaw(string command)
        {
            var execution = new CheatExecution(s_NextExecutionId++, null, Array.Empty<object>(), Time.realtimeSinceStartup)
            {
                Command = command?.Trim()
            };
            SendToServer(execution);
            return execution;
        }

        /// <summary>
        /// 서버 응답 보고. 응답에 요청 식별자가 없으므로 전송 중인 한 건에 대응시킨다 (한 번에 하나만 보낸다).
        /// ICheatServerTransport.TrySend 안에서 동기적으로 호출해도 된다.
        /// 메인 스레드가 아닌 곳(네트워크 콜백 등)에서 부르면 다음 프레임에 메인 스레드에서 처리하고 true를 반환한다.
        /// 큐가 멈춘 상태에서 온 응답은 시간 초과된 요청의 늦은 응답으로 보고 버린 뒤 큐를 재개한다.
        /// 메인 스레드에서 처리했는데 대응할 건이 없으면 false.
        /// </summary>
        public static bool ReportServerResult(bool success, string message = null)
        {
            if (s_MainThreadId != -1 && Thread.CurrentThread.ManagedThreadId != s_MainThreadId)
            {
                s_PendingReports.Enqueue((success, message));
                return true;
            }
            return HandleServerResult(success, message);
        }

        private static bool HandleServerResult(bool success, string message)
        {
            if (s_InFlight == null)
            {
                if (s_ServerQueuePaused)
                {
                    Debug.LogWarning($"[HB_Cheat] Discarded a late server response ({(success ? "success" : "failure")}: {message}). Resuming the server queue.");
                    ResumeCore();
                    return true;
                }
                Debug.LogWarning("[HB_Cheat] Server result received but no cheat is waiting.");
                return false;
            }

            var execution = s_InFlight;
            s_InFlight = null;
            Finish(execution, success ? CheatStatus.Succeeded : CheatStatus.Failed, message, notify: true);
            SendNext();
            return true;
        }

        /// <summary>
        /// 멈춘 서버 큐를 재개한다. 시간 초과된 요청의 응답이 끝내 오지 않을 때, 재접속·재동기화 직후에 부른다.
        /// (재개 뒤 늦은 응답이 오면 다음 요청의 결과로 처리되므로, 연결을 새로 맺은 다음에 부르는 것이 안전하다)
        /// 이미 받아 두고 아직 처리하지 않은 응답은 재접속 전 연결의 응답이므로 버린다. 종료 중에는 무시한다.
        /// </summary>
        public static void ResumeServerQueue()
        {
            if (!s_ServerQueuePaused || s_IsShuttingDown) return;

            int discarded = 0;
            while (s_PendingReports.TryDequeue(out _)) discarded++;
            if (discarded > 0)
                Debug.LogWarning($"[HB_Cheat] Discarded {discarded} server response(s) received before resuming.");

            ResumeCore();
        }

        // 늦은 응답이 도착해 자동 재개할 때도 쓴다 (종료 중이어도 멈춤만 풀고, 전송은 SendNext가 막는다)
        private static void ResumeCore()
        {
            if (!s_ServerQueuePaused) return;
            s_ServerQueuePaused = false;
            s_ServerQueuePauseReason = null;
            foreach (var queued in s_ServerQueue)
                queued.Message = "Queued";
            RaiseServerQueueChanged();
            SendNext();
        }

        private static void PauseServerQueue(string reason)
        {
            s_ServerQueuePaused = true;
            s_ServerQueuePauseReason = reason;
            foreach (var queued in s_ServerQueue)
                queued.Message = PausedMessage;
            RaiseServerQueueChanged();
        }

        private static string PausedMessage => $"Paused: {s_ServerQueuePauseReason}. Waiting for the late response or ResumeServerQueue";

        public static CheatExecution FindExecution(int id)
        {
            for (int i = s_History.Count - 1; i >= 0; i--)
            {
                if (s_History[i].Id == id) return s_History[i];
            }
            return null;
        }

        /// <summary>이 치트에 마지막으로 쓴 인자 값 (없으면 null)</summary>
        public static object[] GetLastValues(string cheatId)
        {
            return cheatId != null && s_LastValues.TryGetValue(cheatId, out var values) ? (object[])values.Clone() : null;
        }

        public static IReadOnlyList<CheatOption> GetOptions(CheatDefinition definition, int argIndex, object[] values, out string error)
        {
            return CheatOptionCache.Get(definition, argIndex, values, out error);
        }

        /// <summary>Provider 캐시를 모두 버린다 (테이블 리로드 후 등).</summary>
        public static void InvalidateOptions() => CheatOptionCache.Clear();

        private static void RunLocal(CheatExecution execution)
        {
            var invoker = execution.Definition.LocalInvoker;
            if (invoker == null)
            {
                Finish(execution, CheatStatus.Failed, "No local invoker", notify: false);
                return;
            }

            try
            {
                var result = invoker(new CheatArgs(execution.Values));
                Finish(execution, result.Success ? CheatStatus.Succeeded : CheatStatus.Failed, result.Message, notify: false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(execution, CheatStatus.Failed, e.Message, notify: false);
            }
        }

        private static void SendToServer(CheatExecution execution)
        {
            // 시간 초과 처리를 러너가 하므로 초기화 전에는 보내지 않는다
            if (!s_IsInitialized)
            {
                AddHistory(execution);
                Finish(execution, CheatStatus.Failed, "CheatSystem is not initialized", notify: true);
                return;
            }
            if (string.IsNullOrEmpty(execution.Command))
            {
                AddHistory(execution);
                Finish(execution, CheatStatus.Failed, "Empty command", notify: true);
                return;
            }

            // 메시지와 대기열을 먼저 반영한 뒤 알린다 (히스토리 화면의 대기 건수·메시지가 실제와 맞도록)
            if (s_ServerQueuePaused) execution.Message = PausedMessage;
            else if (s_InFlight != null) execution.Message = "Queued (waiting for the previous server cheat)";
            s_ServerQueue.Enqueue(execution);
            AddHistory(execution);
            SendNext();
        }

        private static void SendNext()
        {
            // TrySend 안의 동기 응답이 다시 SendNext를 부르면 재귀 대신 바깥 반복문이 이어서 처리한다
            if (s_IsSendingNext) return;
            s_IsSendingNext = true;
            try { SendNextLoop(); }
            finally { s_IsSendingNext = false; }
        }

        private static void SendNextLoop()
        {
            // 종료 중(Bridge.Stop의 동기 응답 등)에는 다음 건을 보내지 않는다. 남은 건은 Shutdown이 실패로 정리한다
            while (s_IsInitialized && s_InFlight == null && !s_ServerQueuePaused && s_ServerQueue.Count > 0)
            {
                var execution = s_ServerQueue.Dequeue();
                if (ServerTransport == null)
                {
                    Finish(execution, CheatStatus.Failed, "No ICheatServerTransport registered (CheatSystem.ServerTransport)", notify: true);
                    continue;
                }

                // 전송 중 동기 응답(ReportServerResult)도 받을 수 있도록 먼저 등록한다
                execution.Message = null;
                execution.SentAt = Time.realtimeSinceStartup;
                s_InFlight = execution;

                bool sent;
                string error;
                try
                {
                    sent = ServerTransport.TrySend(execution.Command, out error);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    sent = false;
                    error = e.Message;
                }

                if (!sent && s_InFlight == execution)
                {
                    s_InFlight = null;
                    Finish(execution, CheatStatus.Failed, error ?? "Send failed", notify: true);
                }
                else if (sent && s_InFlight == execution)
                {
                    RaiseExecutionChanged(execution);
                }
            }
        }

        /// <summary>러너가 매 프레임 호출 (서버 응답 시간 초과 처리)</summary>
        internal static void Tick()
        {
            while (s_PendingReports.TryDequeue(out var report))
                HandleServerResult(report.Success, report.Message);

            if (s_InFlight == null) return;

            // 0 이하로 잘못 넣어도 매 프레임 시간 초과가 나지 않게 한다 (에디터 설정과 같은 하한)
            float timeout = Mathf.Max(0.5f, Options.ServerTimeoutSeconds);
            if (Time.realtimeSinceStartup - s_InFlight.SentAt <= timeout) return;

            var execution = s_InFlight;
            s_InFlight = null;
            // 응답에 요청 식별자가 없어 늦게 온 응답이 다음 요청에 붙을 수 있으므로, 다음 건을 보내지 않고 멈춘다
            PauseServerQueue($"#{execution.Id} timed out");
            Finish(execution, CheatStatus.TimedOut, $"No server response in {timeout:0.#}s. Server queue paused", notify: true);
        }

        private static void Finish(CheatExecution execution, CheatStatus status, string message, bool notify)
        {
            execution.Status = status;
            execution.Message = message;
            execution.FinishedAt = Time.realtimeSinceStartup;

            if (status == CheatStatus.Failed || status == CheatStatus.TimedOut)
                Debug.LogWarning($"[HB_Cheat] {status}: {execution.Describe()} — {message}");

            if (notify) RaiseExecutionChanged(execution);
        }

        // 구독자(패널 등) 하나의 예외가 큐 진행·종료 정리를 끊지 않도록 구독자마다 격리한다
        private static void RaiseExecutionChanged(CheatExecution execution)
        {
            var handlers = ExecutionChanged;
            if (handlers == null) return;
            foreach (Action<CheatExecution> handler in handlers.GetInvocationList())
            {
                try { handler(execution); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void RaiseServerQueueChanged()
        {
            var handlers = ServerQueueChanged;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void AddHistory(CheatExecution execution)
        {
            s_History.Add(execution);
            int capacity = Math.Max(1, Options.HistoryCapacity);
            while (s_History.Count > capacity)
            {
                // 대기 중인 건은 응답 처리를 위해 대기 큐에 남아 있다
                s_History.RemoveAt(0);
            }
            RaiseExecutionChanged(execution);
        }

        private static bool ContainsValue(IReadOnlyList<CheatOption> options, object value)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (Equals(options[i].Value, value)) return true;
            }
            return false;
        }

        #endregion

        #region Favorites

        public static bool IsFavorite(string cheatId)
        {
            EnsureFavoritesLoaded();
            return cheatId != null && s_Favorites.Contains(cheatId);
        }

        public static void SetFavorite(string cheatId, bool favorite)
        {
            if (string.IsNullOrEmpty(cheatId)) return;
            EnsureFavoritesLoaded();

            bool changed = favorite ? s_Favorites.Add(cheatId) : s_Favorites.Remove(cheatId);
            if (!changed) return;

            PlayerPrefs.SetString(FavoritesPrefsKey, string.Join("\n", s_Favorites));
            PlayerPrefs.Save();
            FavoritesChanged?.Invoke();
        }

        private static void EnsureFavoritesLoaded()
        {
            if (s_Favorites != null) return;

            s_Favorites = new HashSet<string>(StringComparer.Ordinal);
            string saved = PlayerPrefs.GetString(FavoritesPrefsKey, string.Empty);
            if (saved.Length == 0) return;

            foreach (var id in saved.Split('\n'))
            {
                if (id.Length > 0) s_Favorites.Add(id);
            }
        }

        #endregion
    }

    /// <summary>
    /// SubsystemRegistration에서 다른 어셈블리가 채우는 값. 실행 순서가 정해져 있지 않아 정리(리셋) 대상에서 뺀다.
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatBootstrapState
    {
        public static CheatRuntimeOptions Options = new CheatRuntimeOptions();
        public static ICheatPanelHost ConsoleHost;
    }
}
