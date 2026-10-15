#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;

/// <summary>
/// 인게임 콘솔 및 백엔드 로그 버퍼를 관리하는 코어 엔진 클래스입니다.
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
        {
            RawMessage = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
            LogType = logType;
            Timestamp = DateTime.Now;

            ParseTagAndMessage(RawMessage, logType, out string parsedTag, out string parsedMessage);
            Tag = parsedTag;
            Message = parsedMessage;
        }

        private static readonly Regex TagRegex = new Regex(@"^#([^#]+)#\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ColonTagRegex = new Regex(@"^([a-zA-Z0-9_\-\.\/]+):\s+(.*)$", RegexOptions.Compiled);

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
    }

    private static readonly List<LogEntry> s_LogHistory = new List<LogEntry>();
    private static readonly Dictionary<string, WatchEntry> s_WatchMap = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);
    private sealed class WatchGetter
    {
        public Func<object> Getter;
        public UnityEngine.Object Owner;
        public bool HasOwner;
    }

    private static readonly ConcurrentDictionary<string, WatchGetter> s_WatchGetters = new ConcurrentDictionary<string, WatchGetter>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_KnownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LogChannel> s_ChannelCache = new ConcurrentDictionary<string, LogChannel>(StringComparer.OrdinalIgnoreCase);

    private static readonly object s_Lock = new object();

    // 다른 스레드(Task, Job 등)에서 들어온 로그는 큐에 쌓았다가 메인 스레드에서 추가한다 (UI 이벤트는 메인 스레드에서만 발생)
    private readonly struct PendingLog
    {
        public readonly string Condition;
        public readonly string StackTrace;
        public readonly LogType Type;

        public PendingLog(string condition, string stackTrace, LogType type)
        {
            Condition = condition;
            StackTrace = stackTrace;
            Type = type;
        }
    }

    /// <summary>
    /// PlayerLoop에 끼워 넣는 대기 로그 처리 시스템의 식별 타입
    /// </summary>
    private struct HLogFlushPendingLogs { }

    private static readonly ConcurrentQueue<PendingLog> s_PendingLogs = new ConcurrentQueue<PendingLog>();
    private static int s_PendingLogCount;
    // 다른 스레드에서 Watch/Unwatch를 호출했을 때 이벤트를 메인 스레드에서 발생시키기 위한 큐
    private static readonly ConcurrentQueue<Action> s_PendingEvents = new ConcurrentQueue<Action>();
    private static int s_MainThreadId = -1;

    private static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == s_MainThreadId;

    // History 배열을 복사하지 않고 개수를 조회할 수 있도록 추가/삭제 시점에 갱신한다 (s_Lock 안에서만 변경)
    private static int s_InfoCount;
    private static int s_WarningCount;
    private static int s_ErrorCount;
    private static int s_UntaggedCount;
    private const int MaxLogCount = 1000;
    private static bool s_IsListeningUnityLogs = false;

    public static event Action<LogEntry> OnLogAdded;
    public static event Action OnLogsCleared;
    public static event Action<string, string> OnWatchUpdated;
    public static event Action<string> OnWatchRemoved;
    public static event Action<string> OnTagDiscovered;

    private static bool s_IsLogEnabled = true;

    /// <summary>
    /// 전역 로그 활성화/비활성화 (에디터 및 런타임)
    /// false일 경우 로그 기록 및 인게임/에디터 콘솔 출력이 차단됩니다.
    /// </summary>
    public static bool IsLogEnabled
    {
        get => s_IsLogEnabled;
        set
        {
            if (s_IsLogEnabled != value)
            {
                s_IsLogEnabled = value;
                OnLogEnableChanged?.Invoke(value);
            }
        }
    }

    public static event Action<bool> OnLogEnableChanged;

    public static bool IsInitialized => s_IsListeningUnityLogs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lock (s_Lock)
        {
            s_LogHistory.Clear();
            ResetLogCounts();
            s_WatchMap.Clear();
            s_WatchGetters.Clear();
            s_KnownTags.Clear();
            s_ChannelCache.Clear();
        }
        while (s_PendingLogs.TryDequeue(out _)) { }
        while (s_PendingEvents.TryDequeue(out _)) { }
        Interlocked.Exchange(ref s_PendingLogCount, 0);
        s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        s_IsListeningUnityLogs = false;
        DiscoverAllChannels();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitEditorPolling()
    {
        s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        UnityEditor.EditorApplication.update -= PollWatchGettersInEditor;
        UnityEditor.EditorApplication.update += PollWatchGettersInEditor;
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
    }

    private static void PollWatchGettersInEditor()
    {
        // 에디터 모드(플레이 중이 아닐 때)에는 PlayerLoop가 돌지 않으므로 여기서 대기 로그를 처리한다
        FlushPendingLogs();

        if (UnityEditor.EditorApplication.isPlaying)
        {
            PollWatchGetters();
        }
    }

    private static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            s_WatchGetters.Clear();
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void AutoInitialize()
    {
        Initialize();
    }

    /// <summary>
    /// 로그 수집을 시작합니다. 메인 스레드에서 호출해야 합니다.
    /// </summary>
    public static void Initialize()
    {
        DiscoverAllChannels();

        if (s_IsListeningUnityLogs) return;

        s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        InstallFlushIntoPlayerLoop();

        lock (s_Lock)
        {
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
            s_IsListeningUnityLogs = true;
        }
    }

    public static void Shutdown()
    {
        if (!s_IsListeningUnityLogs) return;

        lock (s_Lock)
        {
            Application.logMessageReceivedThreaded -= HandleUnityLogThreaded;
            s_IsListeningUnityLogs = false;
        }
    }

    public static IReadOnlyList<LogEntry> History
    {
        get
        {
            lock (s_Lock)
            {
                return s_LogHistory.ToArray();
            }
        }
    }

    /// <summary>
    /// 현재 보관 중인 로그 개수 (History와 달리 배열을 복사하지 않음)
    /// </summary>
    public static int HistoryCount
    {
        get
        {
            lock (s_Lock)
            {
                return s_LogHistory.Count;
            }
        }
    }

    /// <summary>
    /// 로그 종류별 개수. error에는 Error/Assert/Exception이 포함됩니다.
    /// </summary>
    public static void GetLogTypeCounts(out int info, out int warning, out int error)
    {
        lock (s_Lock)
        {
            info = s_InfoCount;
            warning = s_WarningCount;
            error = s_ErrorCount;
        }
    }

    /// <summary>
    /// 태그가 없는 로그가 하나라도 있는지 여부
    /// </summary>
    public static bool HasUntaggedLogs
    {
        get
        {
            lock (s_Lock)
            {
                return s_UntaggedCount > 0;
            }
        }
    }

    public static IReadOnlyDictionary<string, WatchEntry> Watches
    {
        get
        {
            lock (s_Lock)
            {
                return new Dictionary<string, WatchEntry>(s_WatchMap);
            }
        }
    }

    public static IReadOnlyCollection<string> KnownTags
    {
        get
        {
            lock (s_Lock)
            {
                return new List<string>(s_KnownTags);
            }
        }
    }

    /// <summary>
    /// 새로운 채널 태그를 등록합니다. 이미 등록되어 있다면 무시됩니다.
    /// </summary>
    public static void RegisterTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        tag = tag.Trim();
        bool newTag = false;
        lock (s_Lock)
        {
            newTag = s_KnownTags.Add(tag);
        }
        if (newTag)
        {
            OnTagDiscovered?.Invoke(tag);
        }
    }

    /// <summary>
    /// 현재 프로젝트에 정의된 모든 채널(내장, HLog 정적 클래스, 설정 에셋)을 검색하여 태그 목록에 사전 등록합니다.
    /// 로그가 한 번도 찍히지 않아도 필터 탭에 즉시 표시됩니다.
    /// </summary>
    public static void DiscoverAllChannels()
    {
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
                    var a = assemblies[i];
                    var t = a.GetType("HLog");
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
                    var a = assemblies[i];
                    var t = a.GetType("HLog");
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
        catch { }

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
        catch { }
#endif
    }

    /// <summary>
    /// 지정된 태그명의 LogChannel 인스턴스를 캐싱하여 반환합니다.
    /// 정적 코드 생성 없이도 동적으로 HLog.Get("채널명").Log(...) 형태의 호출을 지원합니다.
    /// </summary>
    public static LogChannel GetChannel(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) tagName = "Common";
        RegisterTag(tagName);
        return s_ChannelCache.GetOrAdd(tagName, static tag => new LogChannel(tag));
    }

    [HideInCallstack]
    public static void Watch(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) return;

        string valStr;
        if (value == null)
        {
            valStr = "null";
        }
        else if (value is UnityEngine.Object uObj)
        {
            if (uObj == null)
            {
                valStr = "null";
            }
            else
            {
                valStr = string.IsNullOrEmpty(uObj.name) ? uObj.GetType().Name : $"{uObj.name} ({uObj.GetType().Name})";
            }
        }
        else
        {
            valStr = value.ToString();
        }

        bool valueChanged = false;
        lock (s_Lock)
        {
            if (s_WatchMap.TryGetValue(key, out var entry))
            {
                if (!string.Equals(entry.Value, valStr, StringComparison.Ordinal))
                {
                    entry.Value = valStr;
                    entry.Timestamp = DateTime.Now;
                    valueChanged = true;
                }
            }
            else
            {
                entry = new WatchEntry(key, valStr);
                s_WatchMap[key] = entry;
                valueChanged = true;
            }
        }

        if (valueChanged)
        {
            RaiseOnMainThread(() => OnWatchUpdated?.Invoke(key, valStr));
        }
    }

    /// <summary>
    /// 변수나 프로퍼티를 람다(게터) 형태로 등록하여 매 프레임 실시간 모니터링합니다.
    /// owner를 넘기면 owner가 파괴될 때 자동으로 해제됩니다. 넘기지 않으면 Unwatch로 직접 해제해야 합니다.
    /// 예: HLog.Common.Watch("Player/ImageSlot", () => m_Image, this);
    /// </summary>
    [HideInCallstack]
    public static void Watch(string key, Func<object> getter, UnityEngine.Object owner = null)
    {
        if (string.IsNullOrEmpty(key) || getter == null) return;
        s_WatchGetters[key] = new WatchGetter { Getter = getter, Owner = owner, HasOwner = !ReferenceEquals(owner, null) };

        try
        {
            
            Watch(key, getter());
        }
        catch (Exception ex)
        {
            Watch(key, $"<Error: {ex.Message}>");
        }
    }

    [HideInCallstack]
    public static void Watch<T>(string key, Func<T> getter, UnityEngine.Object owner = null)
    {
        if (getter == null) return;
        Watch(key, () => (object)getter(), owner);
    }

    /// <summary>
    /// Watch 항목(값/게터)을 제거합니다.
    /// </summary>
    public static void Unwatch(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        bool removedGetter = s_WatchGetters.TryRemove(key, out _);
        bool removedEntry;
        lock (s_Lock)
        {
            removedEntry = s_WatchMap.Remove(key);
        }

        if (removedGetter || removedEntry)
        {
            RaiseOnMainThread(() => OnWatchRemoved?.Invoke(key));
        }
    }

    /// <summary>
    /// 등록된 람다/게터 Watch 변수들을 일괄 평가하여 변경사항이 있으면 반영합니다.
    /// </summary>
    public static void PollWatchGetters()
    {
        if (s_WatchGetters.IsEmpty) return;

        foreach (var kvp in s_WatchGetters)
        {
            // 소유자가 파괴되면, 파괴된 오브젝트를 캡처한 람다가 계속 실행되지 않도록 자동 해제
            if (kvp.Value.HasOwner && kvp.Value.Owner == null)
            {
                Unwatch(kvp.Key);
                continue;
            }

            try
            {
                object val = kvp.Value.Getter?.Invoke();
                Watch(kvp.Key, val);
            }
            catch (Exception ex)
            {
                Watch(kvp.Key, $"<Error: {ex.Message}>");
            }
        }
    }

    private static void HandleUnityLogThreaded(string condition, string stackTrace, LogType type)
    {
        if (!s_IsLogEnabled) return;

        if (IsMainThread)
        {
            HandleUnityLog(condition, stackTrace, type);
            return;
        }

        s_PendingLogs.Enqueue(new PendingLog(condition, stackTrace, type));

        // 처리 시스템이 빠진 경우(다른 코드가 PlayerLoop를 교체 등)에도 큐가 무한히 커지지 않도록 오래된 것부터 버린다
        if (Interlocked.Increment(ref s_PendingLogCount) > MaxLogCount && s_PendingLogs.TryDequeue(out _))
        {
            Interlocked.Decrement(ref s_PendingLogCount);
        }
    }

    /// <summary>
    /// 메인 스레드면 바로 실행하고, 아니면 다음 FlushPendingLogs 때 메인 스레드에서 실행합니다.
    /// </summary>
    private static void RaiseOnMainThread(Action raise)
    {
        if (IsMainThread)
            raise();
        else
            s_PendingEvents.Enqueue(raise);
    }

    /// <summary>
    /// 다른 스레드에서 들어온 대기 로그와 Watch 이벤트를 처리합니다. 메인 스레드에서만 동작합니다.
    /// (런타임은 PlayerLoop Update와 인게임 UI Update, 에디터 모드는 EditorApplication.update에서 자동 호출)
    /// </summary>
    public static void FlushPendingLogs()
    {
        if (s_PendingLogs.IsEmpty && s_PendingEvents.IsEmpty) return;
        if (!IsMainThread) return;

        while (s_PendingEvents.TryDequeue(out var raise))
        {
            raise();
        }

        while (s_PendingLogs.TryDequeue(out var log))
        {
            Interlocked.Decrement(ref s_PendingLogCount);
            // 다른 스레드 로그는 스택을 메인 스레드에서 다시 뽑으면 엉뚱한 스택이 되므로 받은 그대로 기록
            AddEntry(log.Condition, log.StackTrace, log.Type);
        }
    }

    private static void InstallFlushIntoPlayerLoop()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (loop.subSystemList == null) return;

        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            ref var phase = ref loop.subSystemList[i];
            if (phase.type != typeof(UnityEngine.PlayerLoop.Update)) continue;

            var systems = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            if (Array.Exists(systems, s => s.type == typeof(HLogFlushPendingLogs)))
                return;

            var newSystems = new PlayerLoopSystem[systems.Length + 1];
            newSystems[0] = new PlayerLoopSystem
            {
                type = typeof(HLogFlushPendingLogs),
                updateDelegate = FlushPendingLogs
            };
            Array.Copy(systems, 0, newSystems, 1, systems.Length);
            phase.subSystemList = newSystems;

            PlayerLoop.SetPlayerLoop(loop);
            return;
        }

        UnityEngine.Debug.LogWarning("[HLogCore] PlayerLoop Update not found. Logs from other threads are added only while the in-game console UI or editor is updating.");
    }

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        if (!s_IsLogEnabled) return;
        if (string.IsNullOrEmpty(stackTrace))
        {
#if UNITY_EDITOR
            stackTrace = StackTraceUtility.ExtractStackTrace();
#endif
        }
        AddEntry(condition, stackTrace, type);
    }

    private static LogEntry AddEntry(string message, string stackTrace, LogType type)
    {
        var entry = new LogEntry(message, stackTrace, type);
        bool newTagFound = false;

        lock (s_Lock)
        {
            if (s_LogHistory.Count >= MaxLogCount)
            {
                UpdateLogCounts(s_LogHistory[0], -1);
                s_LogHistory.RemoveAt(0);
            }
            s_LogHistory.Add(entry);
            UpdateLogCounts(entry, 1);

            if (!string.IsNullOrEmpty(entry.Tag) && s_KnownTags.Add(entry.Tag))
            {
                newTagFound = true;
            }
        }

        if (newTagFound)
        {
            OnTagDiscovered?.Invoke(entry.Tag);
        }

        OnLogAdded?.Invoke(entry);
        return entry;
    }

    public static void Clear()
    {
        lock (s_Lock)
        {
            s_LogHistory.Clear();
            ResetLogCounts();
        }
        DiscoverAllChannels();
        OnLogsCleared?.Invoke();
    }

    private static void UpdateLogCounts(LogEntry entry, int delta)
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

    private static void ResetLogCounts()
    {
        s_InfoCount = 0;
        s_WarningCount = 0;
        s_ErrorCount = 0;
        s_UntaggedCount = 0;
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
}
