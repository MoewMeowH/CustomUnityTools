#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 인게임 콘솔 및 백엔드 로그 버퍼를 관리하는 코어 엔진 클래스입니다.
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public static class HLogCore
{
    public class LogEntry
    {
        public string Message { get; set; }
        public string RawMessage { get; set; }
        public string Tag { get; set; }
        public string StackTrace { get; set; }
        public LogType LogType { get; set; }
        public DateTime Timestamp { get; set; }

        public string TimeString => Timestamp.ToString("HH:mm:ss");

        public LogEntry(string message, string stackTrace, LogType logType)
        {
            RawMessage = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
            LogType = logType;
            Timestamp = DateTime.Now;

            ParseTagAndMessage(RawMessage, out string parsedTag, out string parsedMessage);
            Tag = parsedTag;
            Message = parsedMessage;
        }

        private static readonly Regex TagRegex = new Regex(@"^#([^#]+)#\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ColonTagRegex = new Regex(@"^([a-zA-Z0-9_\-\.\/]+):\s+(.*)$", RegexOptions.Compiled);

        private static void ParseTagAndMessage(string raw, out string tag, out string msg)
        {
            var matchSharp = TagRegex.Match(raw);
            if (matchSharp.Success)
            {
                tag = matchSharp.Groups[1].Value.Trim();
                msg = matchSharp.Groups[2].Value;
                return;
            }

            var matchColon = ColonTagRegex.Match(raw);
            if (matchColon.Success && matchColon.Groups[1].Value.Length < 32 && !matchColon.Groups[1].Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                tag = matchColon.Groups[1].Value.Trim();
                msg = matchColon.Groups[2].Value;
                return;
            }

            tag = string.Empty;
            msg = raw;
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
    private static readonly HashSet<string> s_KnownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LogChannel> s_ChannelCache = new ConcurrentDictionary<string, LogChannel>(StringComparer.OrdinalIgnoreCase);

    private static readonly object s_Lock = new object();
    private const int MaxLogCount = 1000;
    private static bool s_IsListeningUnityLogs = false;

    public static event Action<LogEntry> OnLogAdded;
    public static event Action OnLogsCleared;
    public static event Action<string, string> OnWatchUpdated;
    public static event Action<string> OnTagDiscovered;

    public static bool IsInitialized => s_IsListeningUnityLogs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lock (s_Lock)
        {
            s_LogHistory.Clear();
            s_WatchMap.Clear();
            s_KnownTags.Clear();
            s_ChannelCache.Clear();
        }
        s_IsListeningUnityLogs = false;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void AutoInitialize()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (s_IsListeningUnityLogs) return;

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
            Application.logMessageReceived -= HandleUnityLog;
            Application.logMessageReceived += HandleUnityLog;
            s_IsListeningUnityLogs = true;
        }
    }

    public static void Shutdown()
    {
        if (!s_IsListeningUnityLogs) return;

        lock (s_Lock)
        {
            Application.logMessageReceived -= HandleUnityLog;
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
    /// 지정된 태그명의 LogChannel 인스턴스를 캐싱하여 반환합니다.
    /// 정적 코드 생성 없이도 동적으로 HLog.Get("채널명").Log(...) 형태의 호출을 지원합니다.
    /// </summary>
    public static LogChannel GetChannel(string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) tagName = "Common";
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
            valStr = uObj == null ? "null" : uObj.name;
        }
        else
        {
            valStr = value.ToString();
        }

        lock (s_Lock)
        {
            if (s_WatchMap.TryGetValue(key, out var entry))
            {
                entry.Value = valStr;
                entry.Timestamp = DateTime.Now;
            }
            else
            {
                entry = new WatchEntry(key, valStr);
                s_WatchMap[key] = entry;
            }
        }

        OnWatchUpdated?.Invoke(key, valStr);
    }

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
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
                s_LogHistory.RemoveAt(0);
            }
            s_LogHistory.Add(entry);

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
            s_KnownTags.Clear();
        }
        OnLogsCleared?.Invoke();
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

// 글로벌 네임스페이스 레거시 호환성 별칭
[Obsolete("HB_Log is deprecated. Use HLog.Common instead.")]
public static class HB_Log
{
    public static void Log(object message, UnityEngine.Object context = null) => UnityEngine.Debug.Log(message, context);
    public static void LogWarning(object message, UnityEngine.Object context = null) => UnityEngine.Debug.LogWarning(message, context);
    public static void Error(object message, UnityEngine.Object context = null) => UnityEngine.Debug.LogError(message, context);
    public static void LogError(object message, UnityEngine.Object context = null) => UnityEngine.Debug.LogError(message, context);
    public static void LogException(Exception exception, UnityEngine.Object context = null) => UnityEngine.Debug.LogException(exception, context);
    public static void Watch(string key, object value) => HLogCore.Watch(key, value);
    public static void Clear() => HLogCore.Clear();
}
