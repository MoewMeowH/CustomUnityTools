#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;

/// <summary>
/// 인게임 콘솔 및 백엔드 로그 버퍼를 관리하는 코어 엔진 클래스입니다.
/// <para>
/// 동작 조건: <c>USE_HLOG</c> 심볼이 정의된 경우에만 수집·Watch·이벤트가 동작합니다(에디터 포함).
/// 심볼이 없으면 공개 API는 남지만 아무 동작도 하지 않고 빈 값을 돌려줍니다.
/// </para>
/// <para>
/// 스레드 규칙: 채널 가져오기(<see cref="GetChannel"/>)와 로그 출력만 어느 스레드에서나 호출할 수 있습니다.
/// 그 밖의 공개 API(Watch 계열, Unwatch, Clear, IsLogEnabled 설정, Initialize, Shutdown, PollWatchGetters, FlushPendingLogs)는
/// 메인 스레드 전용이며, 다른 스레드에서 호출하면 처리하지 않고 API별로 처음 한 번만 에러 로그를 남깁니다.
/// </para>
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public static class HLogCore
{
    public class LogEntry
    {
        public string Message { get; private set; }
        public string RawMessage { get; private set; }
        public string Tag { get; private set; }
        public string StackTrace { get; private set; }
        public LogType LogType { get; private set; }
        public DateTime Timestamp { get; private set; }

        public string TimeString => Timestamp.ToString("HH:mm:ss");

        public LogEntry(string message, string stackTrace, LogType logType)
            : this(message, stackTrace, logType, DateTime.Now, null)
        {
        }

        /// <summary>
        /// 다른 스레드에서 들어온 로그는 수신 시각을, 채널 Exception은 채널 태그를 그대로 쓰기 위한 생성자.
        /// explicitTag가 있으면 메시지에서 태그를 해석하지 않는다.
        /// </summary>
        internal LogEntry(string message, string stackTrace, LogType logType, DateTime timestamp, string explicitTag)
        {
            RawMessage = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
            LogType = logType;
            Timestamp = timestamp;

            if (!string.IsNullOrEmpty(explicitTag))
            {
                Tag = explicitTag;
                Message = RawMessage;
                return;
            }

            ParseTagAndMessage(RawMessage, logType, out string parsedTag, out string parsedMessage);
            Tag = parsedTag;
            Message = parsedMessage;
        }

        // Singleline: 여러 줄 메시지도 태그를 인식하도록 '.'이 줄바꿈까지 포함하게 한다
        private static readonly Regex TagRegex = new Regex(@"^#([^#]+)#\s*(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ColonTagRegex = new Regex(@"^([a-zA-Z0-9_\-\.\/]+):\s+(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);

        private static void ParseTagAndMessage(string raw, LogType logType, out string tag, out string msg)
        {
            var matchSharp = TagRegex.Match(raw);
            if (matchSharp.Success)
            {
                tag = matchSharp.Groups[1].Value.Trim();
                msg = matchSharp.Groups[2].Value;
                return;
            }

            // "NullReferenceException: ..." 처럼 예외 타입 이름이 태그로 등록되지 않도록 예외 로그는 콜론 태그를 해석하지 않는다
            var matchColon = logType == LogType.Exception ? Match.Empty : ColonTagRegex.Match(raw);
            if (matchColon.Success && IsColonTagCandidate(matchColon.Groups[1].Value))
            {
                tag = matchColon.Groups[1].Value.Trim();
                msg = matchColon.Groups[2].Value;
                return;
            }

            tag = string.Empty;
            msg = raw;
        }

        private static bool IsColonTagCandidate(string tag)
        {
            return tag.Length < 32
                   && !tag.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                   && !tag.EndsWith("Exception", StringComparison.Ordinal);
        }
    }

    public class WatchEntry
    {
        public string Key { get; }
        public string Value { get; set; }
        public DateTime Timestamp { get; set; }

        public WatchEntry(string key, string value)
        {
            Key = key;
            Value = value;
            Timestamp = DateTime.Now;
        }

        internal WatchEntry Clone() => new WatchEntry(Key, Value) { Timestamp = Timestamp };
    }

    /// <summary>
    /// 구독 목록을 복사본 배열로 들고 있다가 구독자마다 예외를 격리해 호출한다.
    /// 호출 때마다 GetInvocationList로 배열을 만들지 않도록 추가·제거 때만 새 배열을 만든다.
    /// </summary>
    private sealed class EventList<T> where T : Delegate
    {
        private readonly object m_Gate = new object();
        private T[] m_Handlers = Array.Empty<T>();

        public T[] Snapshot => Volatile.Read(ref m_Handlers);

        public void Add(T handler)
        {
            if (handler == null) return;
            lock (m_Gate)
            {
                var next = new T[m_Handlers.Length + 1];
                Array.Copy(m_Handlers, next, m_Handlers.Length);
                next[m_Handlers.Length] = handler;
                Volatile.Write(ref m_Handlers, next);
            }
        }

        public void Remove(T handler)
        {
            if (handler == null) return;
            lock (m_Gate)
            {
                // 마지막으로 추가된 같은 핸들러부터 하나만 제거 (C# 이벤트의 -= 와 같은 규칙)
                int index = Array.LastIndexOf(m_Handlers, handler);
                if (index < 0) return;
                var next = new T[m_Handlers.Length - 1];
                Array.Copy(m_Handlers, 0, next, 0, index);
                Array.Copy(m_Handlers, index + 1, next, index, m_Handlers.Length - index - 1);
                Volatile.Write(ref m_Handlers, next);
            }
        }
    }

    private static readonly EventList<Action<LogEntry>> s_LogAdded = new EventList<Action<LogEntry>>();
    private static readonly EventList<Action> s_LogsCleared = new EventList<Action>();
    private static readonly EventList<Action<string, string>> s_WatchUpdated = new EventList<Action<string, string>>();
    private static readonly EventList<Action<string>> s_WatchRemoved = new EventList<Action<string>>();
    private static readonly EventList<Action<string>> s_TagDiscovered = new EventList<Action<string>>();
    private static readonly EventList<Action<bool>> s_LogEnableChanged = new EventList<Action<bool>>();

    /// <summary>로그가 기록될 때 (메인 스레드)</summary>
    public static event Action<LogEntry> OnLogAdded { add => s_LogAdded.Add(value); remove => s_LogAdded.Remove(value); }
    /// <summary>Clear 후 (메인 스레드)</summary>
    public static event Action OnLogsCleared { add => s_LogsCleared.Add(value); remove => s_LogsCleared.Remove(value); }
    /// <summary>Watch 값이 바뀔 때 (메인 스레드)</summary>
    public static event Action<string, string> OnWatchUpdated { add => s_WatchUpdated.Add(value); remove => s_WatchUpdated.Remove(value); }
    /// <summary>Watch 항목이 제거될 때 (메인 스레드)</summary>
    public static event Action<string> OnWatchRemoved { add => s_WatchRemoved.Add(value); remove => s_WatchRemoved.Remove(value); }
    /// <summary>새 채널 태그를 처음 알게 될 때 (메인 스레드)</summary>
    public static event Action<string> OnTagDiscovered { add => s_TagDiscovered.Add(value); remove => s_TagDiscovered.Remove(value); }
    /// <summary>IsLogEnabled가 바뀔 때 (메인 스레드)</summary>
    public static event Action<bool> OnLogEnableChanged { add => s_LogEnableChanged.Add(value); remove => s_LogEnableChanged.Remove(value); }

    private const int MaxLogCount = 1000;

    private static readonly ConcurrentDictionary<string, LogChannel> s_ChannelCache = new ConcurrentDictionary<string, LogChannel>(StringComparer.OrdinalIgnoreCase);

#if USE_HLOG
    // ---------------------------------------------------------------------------------------------
    // 상태 (메인 스레드에서만 바뀌는 것은 따로 표시)
    // ---------------------------------------------------------------------------------------------

    // 기록: 고정 크기 링 버퍼. s_Lock 안에서만 바뀐다 (읽기 API가 다른 스레드에서 불릴 수 있으므로)
    private static readonly object s_Lock = new object();
    private static readonly LogEntry[] s_Ring = new LogEntry[MaxLogCount];
    private static int s_RingHead; // 가장 오래된 항목의 위치
    private static int s_RingCount;
    private static int s_InfoCount;
    private static int s_WarningCount;
    private static int s_ErrorCount;
    private static int s_UntaggedCount;
    private static readonly HashSet<string> s_KnownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WatchEntry> s_WatchMap = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);

    // 수집 입구: 실행 여부 · 세대 · 대기 로그를 한 잠금으로 묶는다.
    // Clear/Shutdown은 세대를 올려, 그 전에 접수된 로그(이미 큐에서 꺼낸 것 포함)가 나중에 기록되지 않게 한다.
    private readonly struct PendingLog
    {
        public readonly string Condition;
        public readonly string StackTrace;
        public readonly LogType Type;
        public readonly DateTime Timestamp;
        public readonly string ChannelTag;
        public readonly long Generation;

        public PendingLog(string condition, string stackTrace, LogType type, DateTime timestamp, string channelTag, long generation)
        {
            Condition = condition;
            StackTrace = stackTrace;
            Type = type;
            Timestamp = timestamp;
            ChannelTag = channelTag;
            Generation = generation;
        }
    }

    private static readonly object s_IngressGate = new object();
    private static readonly Queue<PendingLog> s_PendingLogs = new Queue<PendingLog>();
    private static long s_LogGeneration;
    private static bool s_IsRunning;

    // 다른 스레드에서 처음 발견된 태그: 문자열만 담아 메인 스레드에서 이벤트를 낸다
    private static readonly object s_TagGate = new object();
    private static readonly Queue<string> s_PendingTags = new Queue<string>();

    // 프레임당 처리 예산 (모든 Flush 진입점이 공유)
    private const int MaxFlushLogsPerFrame = MaxLogCount;
    private const int MaxFlushTagsPerFrame = 64;
    private static int s_BudgetKey = int.MinValue;
    private static int s_LogBudget;
    private static int s_TagBudget;
    private static bool s_IsFlushing;
#if UNITY_EDITOR
    private static int s_EditorTick;
#endif

    // Watch 게터 (메인 스레드 전용)
    private sealed class GetterRegistration
    {
        public Func<object> Getter;
        public UnityEngine.Object Owner;
        public bool HasOwner;
        public bool HasLastRaw;
        public object LastRaw;

        // 등록형 조건 감시 (Condition이 있으면 값 게터 대신 조건으로 평가)
        public Func<bool> Condition;
        public WatchConditionKind Kind;
        public string Message;
        public string ChannelTag;
        public bool Once;
        public bool LastResult;
        public bool Disarmed;
        public int HitCount;
        public DateTime LastHitTime;
        public bool HasShownState;
        public bool ShownResult;
        public int ShownHitCount;
        public bool ShownDisarmed;
    }

    private static readonly Dictionary<string, GetterRegistration> s_WatchGetters = new Dictionary<string, GetterRegistration>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<KeyValuePair<string, GetterRegistration>> s_GetterPollBuffer = new List<KeyValuePair<string, GetterRegistration>>();
    private static int s_LastPolledFrame = -1;
    private static bool s_IsPolling;

    private static bool s_IsLogEnabled = true;

    // 스레드 검사
    private static int s_MainThreadId;
    private static readonly int[] s_ThreadViolationReported = new int[(int)MainThreadApi.Count];

    // 구독자 예외를 로그로 보고할 때 그 로그를 다시 수집하지 않도록 (재귀 방지)
    [ThreadStatic] private static bool s_SuppressCoreCapture;

    // 채널 Exception 호출 동안의 채널 태그 (같은 스레드에서 동기로 들어오는 로그 콜백이 읽는다)
    [ThreadStatic] private static string s_CurrentChannelTag;

    /// <summary>
    /// PlayerLoop에 끼워 넣는 처리 시스템의 식별 타입
    /// </summary>
    private struct HLogTick { }
#endif

    /// <summary>등록형 조건 감시가 발동했을 때 하는 일</summary>
    public enum WatchConditionKind
    {
        /// <summary>에러 로그 + 에디터 일시정지 (빌드는 에러 로그만)</summary>
        Break,
        /// <summary>리포트 파일 + 에러 로그 + 에디터 일시정지 (빌드는 에러 로그만)</summary>
        Report,
    }

    internal enum MainThreadApi
    {
        Initialize,
        Shutdown,
        Clear,
        SetLogEnabled,
        Watch,
        Unwatch,
        PollWatchGetters,
        FlushPendingLogs,
        ConsoleUI,
        Count
    }

    // ---------------------------------------------------------------------------------------------
    // 공개 상태
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 전역 로그 활성화/비활성화. false면 로그 기록과 콘솔 출력이 멈춥니다. 설정은 메인 스레드 전용.
    /// </summary>
    public static bool IsLogEnabled
    {
#if USE_HLOG
        get => Volatile.Read(ref s_IsLogEnabled);
        set
        {
            if (!RequireMainThread(MainThreadApi.SetLogEnabled)) return;
            if (s_IsLogEnabled == value) return;
            Volatile.Write(ref s_IsLogEnabled, value);
            foreach (var handler in s_LogEnableChanged.Snapshot)
            {
                try { handler(value); }
                catch (Exception ex) { ReportSubscriberException(ex); }
            }
        }
#else
        get => false;
        set { }
#endif
    }

    /// <summary>로그 수집 중인지 여부</summary>
    public static bool IsInitialized
    {
#if USE_HLOG
        get { lock (s_IngressGate) return s_IsRunning; }
#else
        get => false;
#endif
    }

    public static IReadOnlyList<LogEntry> History
    {
        get
        {
#if USE_HLOG
            lock (s_Lock)
            {
                var copy = new LogEntry[s_RingCount];
                for (int i = 0; i < s_RingCount; i++)
                {
                    copy[i] = s_Ring[(s_RingHead + i) % MaxLogCount];
                }
                return copy;
            }
#else
            return Array.Empty<LogEntry>();
#endif
        }
    }

    /// <summary>
    /// 현재 보관 중인 로그 개수 (History와 달리 배열을 복사하지 않음)
    /// </summary>
    public static int HistoryCount
    {
        get
        {
#if USE_HLOG
            lock (s_Lock) return s_RingCount;
#else
            return 0;
#endif
        }
    }

    /// <summary>
    /// 로그 종류별 개수. error에는 Error/Assert/Exception이 포함됩니다.
    /// </summary>
    public static void GetLogTypeCounts(out int info, out int warning, out int error)
    {
#if USE_HLOG
        lock (s_Lock)
        {
            info = s_InfoCount;
            warning = s_WarningCount;
            error = s_ErrorCount;
        }
#else
        info = 0;
        warning = 0;
        error = 0;
#endif
    }

    /// <summary>
    /// 태그가 없는 로그가 하나라도 있는지 여부
    /// </summary>
    public static bool HasUntaggedLogs
    {
        get
        {
#if USE_HLOG
            lock (s_Lock) return s_UntaggedCount > 0;
#else
            return false;
#endif
        }
    }

    private static readonly IReadOnlyDictionary<string, WatchEntry> s_EmptyWatches =
        new ReadOnlyDictionary<string, WatchEntry>(new Dictionary<string, WatchEntry>());

    /// <summary>
    /// Watch 항목의 복사본 (반환값을 바꿔도 내부 상태는 바뀌지 않음). 개수만 필요하면 <see cref="WatchCount"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, WatchEntry> Watches
    {
        get
        {
#if USE_HLOG
            lock (s_Lock)
            {
                if (s_WatchMap.Count == 0) return s_EmptyWatches;
                var copy = new Dictionary<string, WatchEntry>(s_WatchMap.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in s_WatchMap)
                {
                    copy[kvp.Key] = kvp.Value.Clone();
                }
                return copy;
            }
#else
            return s_EmptyWatches;
#endif
        }
    }

    /// <summary>Watch 항목 개수 (복사 없음)</summary>
    public static int WatchCount
    {
        get
        {
#if USE_HLOG
            lock (s_Lock) return s_WatchMap.Count;
#else
            return 0;
#endif
        }
    }

    public static IReadOnlyCollection<string> KnownTags
    {
        get
        {
#if USE_HLOG
            lock (s_Lock) return new List<string>(s_KnownTags);
#else
            return Array.Empty<string>();
#endif
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 수명주기
    // ---------------------------------------------------------------------------------------------

#if USE_HLOG
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CaptureMainThreadId();
        for (int i = 0; i < s_ThreadViolationReported.Length; i++)
        {
            Volatile.Write(ref s_ThreadViolationReported[i], 0);
        }

        Application.logMessageReceivedThreaded -= HandleUnityLogThreaded;
        lock (s_IngressGate)
        {
            s_IsRunning = false;
            s_LogGeneration++;
            s_PendingLogs.Clear();
        }
        lock (s_TagGate)
        {
            s_PendingTags.Clear();
        }
        lock (s_Lock)
        {
            ClearRingLocked();
            s_WatchMap.Clear();
            s_KnownTags.Clear();
        }
        s_WatchGetters.Clear();
        // 채널 캐시는 비우지 않는다: 생성 코드의 정적 채널(HLog.Common.Channel 등)은 [NoAutoStaticsCleanup]이라
        // 플레이 사이에 살아 있으므로, 캐시를 비우면 HLog.Get("Common")이 다른 인스턴스를 만들어 채널 On/Off가 어긋난다.
        // 대신 상태를 처음 값으로 되돌리고 태그를 다시 등록한다.
        foreach (var kvp in s_ChannelCache)
        {
            kvp.Value.ResetToDefaults();
            lock (s_Lock)
            {
                s_KnownTags.Add(kvp.Value.Tag);
            }
        }
        s_LastPolledFrame = -1;
        s_BudgetKey = int.MinValue;
        s_IsFlushing = false;
        s_IsPolling = false;
        // 도메인 리로드를 꺼도 플레이마다 같은 시작 상태가 되도록
        Volatile.Write(ref s_IsLogEnabled, true);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitEditorPolling()
    {
        CaptureMainThreadId();
        UnityEditor.EditorApplication.update -= EditorUpdate;
        UnityEditor.EditorApplication.update += EditorUpdate;
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
    }

    private static void EditorUpdate()
    {
        // 플레이 중에는 PlayerLoop 시스템이 처리한다. 비플레이 중에는 대기 로그·태그만 처리 (게터는 평가하지 않음)
        if (UnityEditor.EditorApplication.isPlaying) return;
        s_EditorTick++;
        FlushInternal();
    }

    private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            s_WatchGetters.Clear();
        }
    }
#endif
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void AutoInitialize()
    {
#if USE_HLOG
        Initialize();
#endif
    }

    /// <summary>
    /// 로그 수집을 시작합니다. 메인 스레드 전용.
    /// </summary>
    public static void Initialize()
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Initialize)) return;

        DiscoverAllChannels();
        InstallTickIntoPlayerLoop();

        lock (s_IngressGate)
        {
            if (s_IsRunning) return;
            s_IsRunning = true;
        }

#if UNITY_EDITOR
        if (Application.GetStackTraceLogType(LogType.Log) == StackTraceLogType.None)
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        }
        if (Application.GetStackTraceLogType(LogType.Warning) == StackTraceLogType.None)
        {
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
        }
#endif
        // Threaded 이벤트는 메인 스레드 로그를 포함한 모든 스레드의 로그를 받는다
        Application.logMessageReceivedThreaded -= HandleUnityLogThreaded;
        Application.logMessageReceivedThreaded += HandleUnityLogThreaded;
#endif
    }

    /// <summary>
    /// 로그 수집 · Watch 평가 · 대기 로그 처리를 모두 멈춥니다. 대기 중인 다른 스레드 로그는 버립니다. 메인 스레드 전용.
    /// 기록과 Watch 값은 남습니다. 다시 시작하려면 Initialize.
    /// </summary>
    public static void Shutdown()
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Shutdown)) return;

        lock (s_IngressGate)
        {
            if (!s_IsRunning) return;
            s_IsRunning = false;
            s_LogGeneration++;
            s_PendingLogs.Clear();
        }
        Application.logMessageReceivedThreaded -= HandleUnityLogThreaded;
#endif
    }

    /// <summary>
    /// 기록을 비웁니다. 아직 처리되지 않은 다른 스레드 대기 로그도 버립니다. 태그와 Watch는 남습니다. 메인 스레드 전용.
    /// </summary>
    public static void Clear()
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Clear)) return;

        lock (s_IngressGate)
        {
            s_LogGeneration++;
            s_PendingLogs.Clear();
        }
        lock (s_Lock)
        {
            ClearRingLocked();
        }

        foreach (var handler in s_LogsCleared.Snapshot)
        {
            try { handler(); }
            catch (Exception ex) { ReportSubscriberException(ex); }
        }
#endif
    }

    // ---------------------------------------------------------------------------------------------
    // 채널 · 태그 (어느 스레드에서나)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 새로운 채널 태그를 등록합니다. 이미 등록되어 있다면 무시됩니다. 어느 스레드에서나 호출할 수 있으며 이벤트는 메인 스레드에서 발생합니다.
    /// </summary>
    public static void RegisterTag(string tag)
    {
#if USE_HLOG
        if (string.IsNullOrWhiteSpace(tag)) return;
        tag = tag.Trim();

        bool newTag;
        lock (s_Lock)
        {
            newTag = s_KnownTags.Add(tag);
        }
        if (!newTag) return;

        if (IsMainThread)
        {
            RaiseTagDiscovered(tag);
        }
        else
        {
            lock (s_TagGate)
            {
                s_PendingTags.Enqueue(tag);
            }
        }
#endif
    }

    /// <summary>
    /// 현재 프로젝트에 정의된 모든 채널(내장, HLog 정적 클래스, 설정 에셋)을 검색하여 태그 목록에 사전 등록합니다.
    /// 로그가 한 번도 찍히지 않아도 필터 탭에 즉시 표시됩니다.
    /// </summary>
    public static void DiscoverAllChannels()
    {
#if USE_HLOG
        RegisterTag("Common");
        RegisterTag("System");

        // 1. Reflection을 통한 HLog 중첩 클래스 스캔 (모든 어셈블리 순회 지원)
        try
        {
            Type hlogType = Type.GetType("HLog, Assembly-CSharp") ?? Type.GetType("HLog");
            if (hlogType == null)
            {
#if UNITY_6000_4_OR_NEWER
                var assemblies = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
                for (int i = 0; i < assemblies.Count; i++)
                {
                    var t = assemblies[i].GetType("HLog");
                    if (t != null)
                    {
                        hlogType = t;
                        break;
                    }
                }
#else
#pragma warning disable UAC0005
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    var t = assemblies[i].GetType("HLog");
                    if (t != null)
                    {
                        hlogType = t;
                        break;
                    }
                }
#pragma warning restore UAC0005
#endif
            }

            if (hlogType != null)
            {
                var nestedTypes = hlogType.GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                foreach (var nt in nestedTypes)
                {
                    if (nt.Name == "Core") continue;
                    RegisterTag(nt.Name);
                }
            }
        }
        catch (Exception ex)
        {
            WarnDiscoveryFailedOnce(ex);
        }

        // 2. 에디터 설정 에셋 채널 스캔
#if UNITY_EDITOR
        try
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:LogChannelGeneratorSettings");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (settings != null)
                {
                    var prop = settings.GetType().GetProperty("Channels");
                    if (prop?.GetValue(settings) is System.Collections.IEnumerable list)
                    {
                        foreach (var item in list)
                        {
                            var fVar = item.GetType().GetField("varName");
                            var fTag = item.GetType().GetField("tagName");
                            string t = fTag?.GetValue(item) as string;
                            if (string.IsNullOrEmpty(t)) t = fVar?.GetValue(item) as string;
                            if (!string.IsNullOrEmpty(t)) RegisterTag(t);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            WarnDiscoveryFailedOnce(ex);
        }
#endif
#endif
    }

    /// <summary>
    /// 지정된 태그명의 LogChannel 인스턴스를 캐싱하여 반환합니다. 어느 스레드에서나 호출할 수 있습니다.
    /// 정적 코드 생성 없이도 동적으로 HLog.Get("채널명").Log(...) 형태의 호출을 지원합니다.
    /// USE_HLOG가 없으면 아무 동작도 하지 않는 채널을 돌려줍니다.
    /// </summary>
    public static LogChannel GetChannel(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) tagName = "Common";
        // LogChannel 생성자가 자신을 캐시에 등록하므로, GetOrAdd가 만든 인스턴스와 캐시 값은 같다
        return s_ChannelCache.GetOrAdd(tagName, static tag => new LogChannel(tag));
    }

    /// <summary>
    /// 직접 생성한 채널(new LogChannel)도 같은 태그의 GetChannel과 같은 인스턴스가 되도록 캐시에 등록한다.
    /// 같은 태그가 이미 있으면 먼저 등록된 것을 유지한다.
    /// </summary>
    internal static void RegisterChannelInstance(LogChannel channel)
    {
        if (channel == null || string.IsNullOrEmpty(channel.Tag)) return;
        s_ChannelCache.TryAdd(channel.Tag, channel);
    }

    // ---------------------------------------------------------------------------------------------
    // Watch (메인 스레드 전용)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 값을 Watch에 기록합니다. 같은 키에 게터 Watch가 있으면 게터를 해제하고 이 값으로 바꿉니다. 메인 스레드 전용.
    /// </summary>
    [Conditional("USE_HLOG")]
    [HideInCallstack]
    public static void Watch(string key, object value)
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Watch)) return;
        if (string.IsNullOrEmpty(key)) return;

        // 나중 호출이 이긴다: 값 Watch는 같은 키의 게터를 해제한다
        s_WatchGetters.Remove(key);
        SetWatchValue(key, ConvertWatchValue(value));
#endif
    }

    /// <summary>
    /// 변수나 프로퍼티를 람다(게터) 형태로 등록하여 매 프레임 실시간 모니터링합니다. 메인 스레드 전용.
    /// owner를 넘기면 owner가 파괴될 때 자동으로 해제됩니다. 넘기지 않으면 Unwatch로 직접 해제해야 합니다.
    /// 예: HLog.Common.Watch("Player/ImageSlot", () => m_Image, this);
    /// </summary>
    [Conditional("USE_HLOG")]
    [HideInCallstack]
    public static void Watch(string key, Func<object> getter, UnityEngine.Object owner = null)
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Watch)) return;
        if (string.IsNullOrEmpty(key) || getter == null) return;

        var registration = new GetterRegistration
        {
            Getter = getter,
            Owner = owner,
            HasOwner = !ReferenceEquals(owner, null),
        };
        s_WatchGetters[key] = registration;

        // 실행 중이면 등록 직후 한 번 평가 (Shutdown 상태면 등록만 두고, 다시 Initialize된 뒤 PlayerLoop 폴링이 평가)
        EvaluateRegistrationIfRunning(key, registration);
#endif
    }

    [Conditional("USE_HLOG")]
    [HideInCallstack]
    public static void Watch<T>(string key, Func<T> getter, UnityEngine.Object owner = null)
    {
#if USE_HLOG
        if (getter == null) return;
        Watch(key, () => (object)getter(), owner);
#endif
    }

    /// <summary>
    /// Watch 항목(값/게터)을 제거합니다. 메인 스레드 전용.
    /// </summary>
    [Conditional("USE_HLOG")]
    public static void Unwatch(string key)
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Unwatch)) return;
        RemoveWatch(key);
#endif
    }

    /// <summary>
    /// 조건을 등록해 매 프레임 평가하고, 거짓에서 참으로 바뀌는 순간 발동합니다 (메인 스레드 전용).
    /// Watch와 같은 키 공간이라 Watch 목록에 함께 보이고 <see cref="Unwatch"/>로 해제됩니다. owner가 파괴되면 자동 해제.
    /// 발동: 채널 에러 로그 + 에디터 일시정지(Report는 리포트 파일도). 빌드는 에러 로그만.
    /// once면 한 번 발동한 뒤 멈추며 <see cref="ResetCondition"/>으로 다시 대기시킬 수 있습니다.
    /// 조건 평가 중 예외가 나면 에러 로그를 한 번 남기고 해제합니다.
    /// </summary>
    [Conditional("USE_HLOG")]
    public static void WatchCondition(string key, Func<bool> condition, WatchConditionKind kind, string message,
        UnityEngine.Object owner = null, bool once = true, string channelTag = null)
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Watch)) return;
        if (string.IsNullOrEmpty(key) || condition == null) return;

        var registration = new GetterRegistration
        {
            Condition = condition,
            Kind = kind,
            Message = message ?? string.Empty,
            ChannelTag = string.IsNullOrEmpty(channelTag) ? "Common" : channelTag,
            Once = once,
            Owner = owner,
            HasOwner = !ReferenceEquals(owner, null),
        };
        s_WatchGetters[key] = registration;
        EvaluateRegistrationIfRunning(key, registration);
#endif
    }

    /// <summary>
    /// 발동해서 멈춘(once) 조건을 다시 대기 상태로 돌립니다. 조건이 지금 참이면 다음 평가에서 다시 발동합니다. 메인 스레드 전용.
    /// </summary>
    [Conditional("USE_HLOG")]
    public static void ResetCondition(string key)
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.Watch)) return;
        if (string.IsNullOrEmpty(key) || !s_WatchGetters.TryGetValue(key, out var registration) || registration.Condition == null) return;

        registration.Disarmed = false;
        registration.LastResult = false;
        registration.HasShownState = false;
        EvaluateRegistrationIfRunning(key, registration);
#endif
    }

    /// <summary>
    /// 등록된 게터를 평가해 값이 바뀌었으면 반영합니다. 프레임당 한 번만 평가합니다. 메인 스레드 전용.
    /// (PlayerLoop에서 자동 호출. 인게임 UI도 PlayerLoop 시스템이 빠진 경우를 대비해 호출)
    /// </summary>
    public static void PollWatchGetters()
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.PollWatchGetters)) return;
        PollInternal();
#endif
    }

    /// <summary>
    /// 다른 스레드에서 들어온 대기 로그와 태그 이벤트를 처리합니다. 프레임당 처리량에 상한이 있습니다. 메인 스레드 전용.
    /// </summary>
    public static void FlushPendingLogs()
    {
#if USE_HLOG
        if (!RequireMainThread(MainThreadApi.FlushPendingLogs)) return;
        FlushInternal();
#endif
    }

    public static string FilterStackTrace(string stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace)) return string.Empty;

        var lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("UnityEngine.Debug") ||
                trimmed.StartsWith("UnityEngine.Logger") ||
                trimmed.StartsWith("UnityEngine.StackTraceUtility") ||
                trimmed.StartsWith("UnityEngine.Application") ||
                trimmed.StartsWith("HLog.") ||
                trimmed.StartsWith("HLog:") ||
                trimmed.StartsWith("LogChannel:") ||
                trimmed.StartsWith("LogChannel.") ||
                trimmed.StartsWith("HB_Log:") ||
                trimmed.StartsWith("HB_Log.") ||
                trimmed.Contains("HLogCore.cs") ||
                trimmed.Contains("LogChannel.cs"))
            {
                continue;
            }

            sb.AppendLine(line);
        }

        string result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? stackTrace : result;
    }

    // ---------------------------------------------------------------------------------------------
    // 내부: 스레드 검사 (패키지 내부 · InGameConsole에서 사용)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 메인 스레드면 true. 아니면 처리하지 않도록 false를 돌려주고 API별로 처음 한 번만 에러 로그를 남긴다.
    /// </summary>
    internal static bool RequireMainThread(MainThreadApi api)
    {
#if USE_HLOG
        if (IsMainThread) return true;

        if (Interlocked.CompareExchange(ref s_ThreadViolationReported[(int)api], 1, 0) == 0)
        {
            UnityEngine.Debug.LogError($"[HLog] {api}: 메인 스레드에서만 호출할 수 있습니다. 이 호출은 처리되지 않았습니다. (같은 API의 이후 위반은 다시 알리지 않습니다)");
        }
        return false;
#else
        return true;
#endif
    }

#if USE_HLOG
    private static bool IsMainThread
    {
        get
        {
            int mainThreadId = Volatile.Read(ref s_MainThreadId);
            if (mainThreadId == 0)
            {
                // 초기화 지점보다 먼저 불린 경우: Unity 메인 스레드에만 있는 SynchronizationContext로 판별해 확정한다.
                // 다른 스레드를 메인으로 등록하지 않도록 확신할 수 없으면 false.
                var context = SynchronizationContext.Current;
                if (context == null || context.GetType().Name != "UnitySynchronizationContext") return false;
                CaptureMainThreadId();
                return true;
            }
            return Thread.CurrentThread.ManagedThreadId == mainThreadId;
        }
    }

    private static void CaptureMainThreadId()
    {
        Volatile.Write(ref s_MainThreadId, Thread.CurrentThread.ManagedThreadId);
    }

    private static int s_DiscoveryWarned;

    private static void WarnDiscoveryFailedOnce(Exception ex)
    {
#if UNITY_EDITOR
        if (Interlocked.Exchange(ref s_DiscoveryWarned, 1) == 0)
        {
            UnityEngine.Debug.LogWarning($"[HLog] 채널 목록을 찾는 중 문제가 생겨 일부 채널이 필터에 미리 표시되지 않을 수 있습니다: {ex.Message}");
        }
#endif
    }

    // ---------------------------------------------------------------------------------------------
    // 내부: 채널 Exception 태그
    // ---------------------------------------------------------------------------------------------

    internal static string PushChannelTag(string tag)
    {
        string previous = s_CurrentChannelTag;
        s_CurrentChannelTag = tag;
        return previous;
    }

    internal static void PopChannelTag(string previous)
    {
        s_CurrentChannelTag = previous;
    }

    // ---------------------------------------------------------------------------------------------
    // 내부: 로그 수집
    // ---------------------------------------------------------------------------------------------

    private static void HandleUnityLogThreaded(string condition, string stackTrace, LogType type)
    {
        if (s_SuppressCoreCapture) return;
        if (!Volatile.Read(ref s_IsLogEnabled)) return;

        DateTime receivedAt = DateTime.Now;
        // 채널 Exception 호출 중이면 그 채널 태그 (예외 로그에만 적용해 무관한 로그에 붙지 않게)
        string channelTag = type == LogType.Exception ? s_CurrentChannelTag : null;

        if (IsMainThread)
        {
            long generation;
            lock (s_IngressGate)
            {
                if (!s_IsRunning) return;
                generation = s_LogGeneration;
            }

            if (string.IsNullOrEmpty(stackTrace))
            {
#if UNITY_EDITOR
                stackTrace = StackTraceUtility.ExtractStackTrace();
#endif
            }
            AddEntry(new LogEntry(condition, stackTrace, type, receivedAt, channelTag), generation);
            return;
        }

        lock (s_IngressGate)
        {
            if (!s_IsRunning) return;

            // 처리 시스템이 빠진 경우에도 무한히 커지지 않도록 가장 오래된 것부터 버린다
            if (s_PendingLogs.Count >= MaxLogCount)
            {
                s_PendingLogs.Dequeue();
            }
            s_PendingLogs.Enqueue(new PendingLog(condition, stackTrace, type, receivedAt, channelTag, s_LogGeneration));
        }
    }

    private static void AddEntry(LogEntry entry, long generation)
    {
        bool newTagFound = false;

        lock (s_Lock)
        {
            // Clear/Shutdown 이후에 도착한 이전 세대 로그는 기록하지 않는다
            if (generation != Volatile.Read(ref s_LogGeneration)) return;

            if (s_RingCount == MaxLogCount)
            {
                UpdateLogCountsLocked(s_Ring[s_RingHead], -1);
                s_Ring[s_RingHead] = entry;
                s_RingHead = (s_RingHead + 1) % MaxLogCount;
            }
            else
            {
                s_Ring[(s_RingHead + s_RingCount) % MaxLogCount] = entry;
                s_RingCount++;
            }
            UpdateLogCountsLocked(entry, 1);

            if (!string.IsNullOrEmpty(entry.Tag) && s_KnownTags.Add(entry.Tag))
            {
                newTagFound = true;
            }
        }

        if (newTagFound)
        {
            RaiseTagDiscovered(entry.Tag);
        }

        foreach (var handler in s_LogAdded.Snapshot)
        {
            try { handler(entry); }
            catch (Exception ex) { ReportSubscriberException(ex); }
        }
    }

    private static void ClearRingLocked()
    {
        Array.Clear(s_Ring, 0, s_Ring.Length);
        s_RingHead = 0;
        s_RingCount = 0;
        s_InfoCount = 0;
        s_WarningCount = 0;
        s_ErrorCount = 0;
        s_UntaggedCount = 0;
    }

    private static void UpdateLogCountsLocked(LogEntry entry, int delta)
    {
        switch (entry.LogType)
        {
            case LogType.Log: s_InfoCount += delta; break;
            case LogType.Warning: s_WarningCount += delta; break;
            default: s_ErrorCount += delta; break;
        }

        if (string.IsNullOrEmpty(entry.Tag))
            s_UntaggedCount += delta;
    }

    // ---------------------------------------------------------------------------------------------
    // 내부: 프레임 처리 (PlayerLoop · 에디터 업데이트 · UI 폴백)
    // ---------------------------------------------------------------------------------------------

    private static void InstallTickIntoPlayerLoop()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (loop.subSystemList == null) return;

        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            ref var phase = ref loop.subSystemList[i];
            if (phase.type != typeof(UnityEngine.PlayerLoop.Update)) continue;

            var systems = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            if (Array.Exists(systems, s => s.type == typeof(HLogTick)))
                return;

            var newSystems = new PlayerLoopSystem[systems.Length + 1];
            newSystems[0] = new PlayerLoopSystem
            {
                type = typeof(HLogTick),
                updateDelegate = TickRuntime
            };
            Array.Copy(systems, 0, newSystems, 1, systems.Length);
            phase.subSystemList = newSystems;

            PlayerLoop.SetPlayerLoop(loop);
            return;
        }

        UnityEngine.Debug.LogWarning("[HLogCore] PlayerLoop Update not found. Logs from other threads and Watch getters are processed only while the in-game console UI is updating.");
    }

    private static void TickRuntime()
    {
        FlushInternal();
        PollInternal();
    }

    private static void RefreshBudget()
    {
        int key;
#if UNITY_EDITOR
        key = Application.isPlaying ? Time.frameCount : unchecked(int.MinValue + s_EditorTick);
#else
        key = Time.frameCount;
#endif
        if (key == s_BudgetKey) return;
        s_BudgetKey = key;
        s_LogBudget = MaxFlushLogsPerFrame;
        s_TagBudget = MaxFlushTagsPerFrame;
    }

    private static void FlushInternal()
    {
        if (s_IsFlushing) return;

        lock (s_IngressGate)
        {
            if (!s_IsRunning) return;
        }

        s_IsFlushing = true;
        try
        {
            RefreshBudget();

            while (s_TagBudget > 0)
            {
                string tag;
                lock (s_TagGate)
                {
                    if (s_PendingTags.Count == 0) break;
                    tag = s_PendingTags.Dequeue();
                }
                s_TagBudget--;
                RaiseTagDiscovered(tag);
            }

            while (s_LogBudget > 0)
            {
                PendingLog log;
                lock (s_IngressGate)
                {
                    if (!s_IsRunning || s_PendingLogs.Count == 0) break;
                    log = s_PendingLogs.Dequeue();
                }
                s_LogBudget--;

                // 다른 스레드 로그는 스택을 메인 스레드에서 다시 뽑으면 엉뚱한 스택이 되므로 받은 그대로 기록
                AddEntry(new LogEntry(log.Condition, log.StackTrace, log.Type, log.Timestamp, log.ChannelTag), log.Generation);
            }
        }
        finally
        {
            s_IsFlushing = false;
        }
    }

    private static void RaiseTagDiscovered(string tag)
    {
        foreach (var handler in s_TagDiscovered.Snapshot)
        {
            try { handler(tag); }
            catch (Exception ex) { ReportSubscriberException(ex); }
        }
    }

    /// <summary>
    /// 구독자 예외를 알린다. 이 로그를 다시 수집하면 같은 구독자가 또 불려 재귀할 수 있으므로 수집하지 않는다.
    /// </summary>
    private static void ReportSubscriberException(Exception ex)
    {
        bool previous = s_SuppressCoreCapture;
        s_SuppressCoreCapture = true;
        try
        {
            UnityEngine.Debug.LogException(ex);
        }
        catch
        {
            // 출력 핸들러 자체의 예외는 다시 보고하지 않는다
        }
        finally
        {
            s_SuppressCoreCapture = previous;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 내부: Watch
    // ---------------------------------------------------------------------------------------------

    private static void PollInternal()
    {
        if (s_IsPolling || s_WatchGetters.Count == 0) return;

        lock (s_IngressGate)
        {
            if (!s_IsRunning) return;
        }

        // PlayerLoop와 UI 폴백이 같은 프레임에 불러도 한 번만 평가
        int frame = Time.frameCount;
        if (frame == s_LastPolledFrame) return;
        s_LastPolledFrame = frame;

        s_IsPolling = true;
        try
        {
            s_GetterPollBuffer.Clear();
            foreach (var kvp in s_WatchGetters)
            {
                s_GetterPollBuffer.Add(kvp);
            }

            for (int i = 0; i < s_GetterPollBuffer.Count; i++)
            {
                // 게터·구독자가 Shutdown을 부르면 남은 평가도 멈춘다
                lock (s_IngressGate)
                {
                    if (!s_IsRunning) break;
                }

                var kvp = s_GetterPollBuffer[i];
                EvaluateRegistration(kvp.Key, kvp.Value);
            }
        }
        finally
        {
            s_GetterPollBuffer.Clear();
            s_IsPolling = false;
        }
    }

    /// <summary>등록이 아직 그 키의 현재 등록인지 (게터·구독자가 평가 중에 해제·교체했으면 false)</summary>
    private static bool IsCurrentRegistration(string key, GetterRegistration registration)
    {
        return s_WatchGetters.TryGetValue(key, out var current) && ReferenceEquals(current, registration);
    }

    /// <summary>Shutdown은 Watch 평가도 멈춘다 — 실행 중이 아니면 등록만 두고 PollWatchGetters가 평가하게 한다</summary>
    private static void EvaluateRegistrationIfRunning(string key, GetterRegistration registration)
    {
        lock (s_IngressGate)
        {
            if (!s_IsRunning) return;
        }
        EvaluateRegistration(key, registration);
    }

    private static void EvaluateRegistration(string key, GetterRegistration registration)
    {
        if (!IsCurrentRegistration(key, registration)) return;

        // 소유자가 파괴되면, 파괴된 오브젝트를 캡처한 람다가 계속 실행되지 않도록 자동 해제
        if (registration.HasOwner && registration.Owner == null)
        {
            RemoveWatch(key);
            return;
        }

        if (registration.Condition != null)
        {
            EvaluateCondition(key, registration);
            return;
        }

        object value;
        string text;
        try
        {
            value = registration.Getter();
        }
        catch (Exception ex)
        {
            if (IsCurrentRegistration(key, registration))
            {
                registration.HasLastRaw = false;
                SetWatchValue(key, $"<Error: {ex.Message}>");
            }
            return;
        }

        if (!IsCurrentRegistration(key, registration)) return;

        // 값 형식 · string은 값이 같으면 문자열 변환과 비교를 건너뛴다 (참조 형식은 내부가 바뀔 수 있어 매번 변환)
        bool comparable = value is string || value is ValueType;
        if (comparable && registration.HasLastRaw && Equals(registration.LastRaw, value)) return;

        try
        {
            text = ConvertWatchValue(value);
        }
        catch (Exception ex)
        {
            text = $"<Error: {ex.Message}>";
        }

        // ToString 안에서 재진입해 해제·교체했을 수 있다
        if (!IsCurrentRegistration(key, registration)) return;

        registration.HasLastRaw = comparable;
        registration.LastRaw = comparable ? value : null;
        SetWatchValue(key, text);
    }

    private static void EvaluateCondition(string key, GetterRegistration registration)
    {
        bool result;
        try
        {
            result = registration.Condition();
        }
        catch (Exception ex)
        {
            // 매 프레임 같은 예외가 반복되지 않도록 한 번 알리고 해제
            if (IsCurrentRegistration(key, registration))
            {
                UnityEngine.Debug.LogError($"[HLog] 조건 '{key}' 평가 중 예외가 나서 해제했습니다: {ex.GetType().Name}: {ex.Message}");
                RemoveWatch(key);
            }
            return;
        }

        // 조건 안에서 해제·교체했을 수 있다
        if (!IsCurrentRegistration(key, registration)) return;

        bool rising = result && !registration.LastResult;
        registration.LastResult = result;

        if (rising && !registration.Disarmed)
        {
            registration.HitCount++;
            registration.LastHitTime = DateTime.Now;
            if (registration.Once) registration.Disarmed = true;

            UpdateConditionText(key, registration);
            FireCondition(key, registration);
            return;
        }

        UpdateConditionText(key, registration);
    }

    /// <summary>표시 문자열은 상태가 바뀔 때만 만든다 (매 프레임 할당하지 않도록)</summary>
    private static void UpdateConditionText(string key, GetterRegistration registration)
    {
        if (registration.HasShownState
            && registration.ShownResult == registration.LastResult
            && registration.ShownHitCount == registration.HitCount
            && registration.ShownDisarmed == registration.Disarmed)
        {
            return;
        }

        registration.HasShownState = true;
        registration.ShownResult = registration.LastResult;
        registration.ShownHitCount = registration.HitCount;
        registration.ShownDisarmed = registration.Disarmed;

        string text = $"[{registration.Kind}] {(registration.LastResult ? "True" : "False")}";
        if (registration.HitCount > 0)
        {
            text += $" · Hit {registration.HitCount}회 (마지막 {registration.LastHitTime:HH:mm:ss})";
        }
        if (registration.Disarmed)
        {
            text += " · 완료";
        }
        SetWatchValue(key, text);
    }

    private static void FireCondition(string key, GetterRegistration registration)
    {
        var channel = GetChannel(registration.ChannelTag);
#if UNITY_EDITOR
        if (registration.Kind == WatchConditionKind.Report)
        {
            // 리포트 파일 + 에러 로그 + 일시정지 (ReportIf와 같은 동작)
            channel.ReportIf(true, $"{key}: {registration.Message}");
            return;
        }
        channel.Error($"[BreakWhen Hit] {key}: {registration.Message}");
        UnityEngine.Debug.Break();
#else
        channel.Error($"[{registration.Kind}When Hit] {key}: {registration.Message}");
#endif
    }

    private static string ConvertWatchValue(object value)
    {
        if (value == null) return "null";

        if (value is UnityEngine.Object uObj)
        {
            if (uObj == null) return "null";
            return string.IsNullOrEmpty(uObj.name) ? uObj.GetType().Name : $"{uObj.name} ({uObj.GetType().Name})";
        }

        return value.ToString() ?? "null";
    }

    private static void SetWatchValue(string key, string text)
    {
        bool changed = false;
        lock (s_Lock)
        {
            if (s_WatchMap.TryGetValue(key, out var entry))
            {
                if (!string.Equals(entry.Value, text, StringComparison.Ordinal))
                {
                    entry.Value = text;
                    entry.Timestamp = DateTime.Now;
                    changed = true;
                }
            }
            else
            {
                s_WatchMap[key] = new WatchEntry(key, text);
                changed = true;
            }
        }

        if (!changed) return;

        foreach (var handler in s_WatchUpdated.Snapshot)
        {
            try { handler(key, text); }
            catch (Exception ex) { ReportSubscriberException(ex); }
        }
    }

    private static void RemoveWatch(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        bool removedGetter = s_WatchGetters.Remove(key);
        bool removedEntry;
        lock (s_Lock)
        {
            removedEntry = s_WatchMap.Remove(key);
        }

        if (!removedGetter && !removedEntry) return;

        foreach (var handler in s_WatchRemoved.Snapshot)
        {
            try { handler(key); }
            catch (Exception ex) { ReportSubscriberException(ex); }
        }
    }
#endif
}
