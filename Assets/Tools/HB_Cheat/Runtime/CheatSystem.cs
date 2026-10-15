#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Generic;
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
            s_LastValues.Clear();
            s_Bridges.Clear();
            s_Favorites = null;
            ServerTransport = null;
            ExecutionChanged = null;
            FavoritesChanged = null;
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
            if (s_IsInitialized) return;
            s_IsInitialized = true;

            var go = new GameObject("[HB_Cheat]");
            if (Options.DontDestroyOnLoad)
                UnityEngine.Object.DontDestroyOnLoad(go);
            s_Runner = go.AddComponent<CheatRunner>();

            // 콘솔이 있으면 탭으로, 없거나 초기화 안 됐으면 독립 패널
            var panel = s_Runner.Panel;
            if (Options.PreferConsoleTab && ConsoleHost != null && ConsoleHost.TryAttach(panel))
                s_ActiveHost = ConsoleHost;
            else
                s_ActiveHost = s_Runner.CreateStandaloneHost();

            for (int i = 0; i < s_Bridges.Count; i++)
                StartBridge(s_Bridges[i]);
        }

        /// <summary>러너 파괴 시 호출된다. 대기 중인 서버 치트는 실패로 끝내고, 콘솔 탭에서 패널을 뗀다.</summary>
        internal static void Shutdown()
        {
            if (!s_IsInitialized) return;
            s_IsInitialized = false;

            for (int i = 0; i < s_Bridges.Count; i++)
            {
                try { s_Bridges[i].Stop(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            if (s_InFlight != null)
            {
                var inFlight = s_InFlight;
                s_InFlight = null;
                Finish(inFlight, CheatStatus.Failed, "Cheat system shut down", notify: true);
            }
            while (s_ServerQueue.Count > 0)
                Finish(s_ServerQueue.Dequeue(), CheatStatus.Failed, "Cheat system shut down", notify: true);

            try { s_ActiveHost?.Detach(); }
            catch (Exception e) { Debug.LogException(e); }

            s_Runner = null;
            s_ActiveHost = null;
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
        /// ICheatServerTransport.TrySend 안에서 동기적으로 호출해도 된다. 전송 중인 건이 없으면 false.
        /// </summary>
        public static bool ReportServerResult(bool success, string message = null)
        {
            if (s_InFlight == null)
            {
                Debug.LogWarning("[HB_Cheat] Server result received but no cheat is waiting (late response after timeout?).");
                return false;
            }

            var execution = s_InFlight;
            s_InFlight = null;
            Finish(execution, success ? CheatStatus.Succeeded : CheatStatus.Failed, message, notify: true);
            SendNext();
            return true;
        }

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
            AddHistory(execution);

            // 시간 초과 처리를 러너가 하므로 초기화 전에는 보내지 않는다
            if (!s_IsInitialized)
            {
                Finish(execution, CheatStatus.Failed, "CheatSystem is not initialized", notify: true);
                return;
            }
            if (string.IsNullOrEmpty(execution.Command))
            {
                Finish(execution, CheatStatus.Failed, "Empty command", notify: true);
                return;
            }

            if (s_InFlight != null) execution.Message = "Queued (waiting for the previous server cheat)";
            s_ServerQueue.Enqueue(execution);
            SendNext();
        }

        private static void SendNext()
        {
            while (s_InFlight == null && s_ServerQueue.Count > 0)
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
                    ExecutionChanged?.Invoke(execution);
                }
            }
        }

        /// <summary>러너가 매 프레임 호출 (서버 응답 시간 초과 처리)</summary>
        internal static void Tick()
        {
            if (s_InFlight == null) return;

            float timeout = Options.ServerTimeoutSeconds;
            if (Time.realtimeSinceStartup - s_InFlight.SentAt <= timeout) return;

            var execution = s_InFlight;
            s_InFlight = null;
            // 이후 늦게 온 응답은 다음 요청의 결과로 잘못 처리될 수 있다 (응답에 요청 식별자가 없음)
            Finish(execution, CheatStatus.TimedOut, $"No server response in {timeout:0.#}s", notify: true);
            SendNext();
        }

        private static void Finish(CheatExecution execution, CheatStatus status, string message, bool notify)
        {
            execution.Status = status;
            execution.Message = message;
            execution.FinishedAt = Time.realtimeSinceStartup;

            if (status == CheatStatus.Failed || status == CheatStatus.TimedOut)
                Debug.LogWarning($"[HB_Cheat] {status}: {execution.Describe()} — {message}");

            if (notify) ExecutionChanged?.Invoke(execution);
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
            ExecutionChanged?.Invoke(execution);
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
