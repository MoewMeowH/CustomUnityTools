using System;
using System.Collections.Generic;
using UnityEngine;

public static class HB_Log
{
    public class HB_LogEntry
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public LogType LogType { get; set; }
        public DateTime Timestamp { get; set; }

        public string TimeString => Timestamp.ToString("HH:mm:ss");

        public HB_LogEntry(string message, string stackTrace, LogType logType)
        {
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
            LogType = logType;
            Timestamp = DateTime.Now;
        }
    }

    private static readonly List<HB_LogEntry> s_LogHistory = new List<HB_LogEntry>();
    private static readonly object s_Lock = new object();
    private const int MaxLogCount = 1000;
    private static bool s_IsListeningUnityLogs = false;
    private static bool s_IsInternalLogging = false;

    public static event Action<HB_LogEntry> OnLogAdded;
    public static event Action OnLogsCleared;

    static HB_Log()
    {
        EnsureInitialized();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lock (s_Lock)
        {
            s_LogHistory.Clear();
        }
        s_IsListeningUnityLogs = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void EnsureInitialized()
    {
        if (!s_IsListeningUnityLogs)
        {
            Application.logMessageReceived -= HandleUnityLog;
            Application.logMessageReceived += HandleUnityLog;
            s_IsListeningUnityLogs = true;
        }
    }

    public static IReadOnlyList<HB_LogEntry> History
    {
        get
        {
            lock (s_Lock)
            {
                return s_LogHistory.ToArray();
            }
        }
    }

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        // HB_Log 내부에서 직접 기록 중이면 중복 방지
        if (s_IsInternalLogging) return;

        AddEntry(condition, stackTrace, type);
    }

    private static HB_LogEntry AddEntry(string message, string stackTrace, LogType type)
    {
        var entry = new HB_LogEntry(message, stackTrace, type);
        lock (s_Lock)
        {
            if (s_LogHistory.Count >= MaxLogCount)
            {
                s_LogHistory.RemoveAt(0);
            }
            s_LogHistory.Add(entry);
        }

        OnLogAdded?.Invoke(entry);
        return entry;
    }

    public static void Log(object message, UnityEngine.Object context = null)
    {
        EnsureInitialized();
        string msg = message != null ? message.ToString() : "null";
        string stack = StackTraceUtility.ExtractStackTrace();

        s_IsInternalLogging = true;
        try
        {
            if (context != null)
                Debug.Log(message, context);
            else
                Debug.Log(message);
        }
        finally
        {
            s_IsInternalLogging = false;
        }

        AddEntry(msg, stack, LogType.Log);
    }

    public static void LogWarning(object message, UnityEngine.Object context = null)
    {
        EnsureInitialized();
        string msg = message != null ? message.ToString() : "null";
        string stack = StackTraceUtility.ExtractStackTrace();

        s_IsInternalLogging = true;
        try
        {
            if (context != null)
                Debug.LogWarning(message, context);
            else
                Debug.LogWarning(message);
        }
        finally
        {
            s_IsInternalLogging = false;
        }

        AddEntry(msg, stack, LogType.Warning);
    }

    public static void LogError(object message, UnityEngine.Object context = null)
    {
        EnsureInitialized();
        string msg = message != null ? message.ToString() : "null";
        string stack = StackTraceUtility.ExtractStackTrace();

        s_IsInternalLogging = true;
        try
        {
            if (context != null)
                Debug.LogError(message, context);
            else
                Debug.LogError(message);
        }
        finally
        {
            s_IsInternalLogging = false;
        }

        AddEntry(msg, stack, LogType.Error);
    }

    public static void LogException(Exception exception, UnityEngine.Object context = null)
    {
        EnsureInitialized();
        string msg = exception != null ? exception.Message : "null exception";
        string stack = exception != null ? exception.StackTrace : StackTraceUtility.ExtractStackTrace();

        s_IsInternalLogging = true;
        try
        {
            if (context != null)
                Debug.LogException(exception, context);
            else
                Debug.LogException(exception);
        }
        finally
        {
            s_IsInternalLogging = false;
        }

        AddEntry(msg, stack, LogType.Exception);
    }

    public static void Clear()
    {
        lock (s_Lock)
        {
            s_LogHistory.Clear();
        }
        OnLogsCleared?.Invoke();
    }
}
