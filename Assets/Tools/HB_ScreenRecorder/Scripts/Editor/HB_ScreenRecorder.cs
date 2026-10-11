#pragma warning disable CS0618

using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public static class HB_ScreenRecorder
{
    [Serializable]
    public class HB_DiagnosticState
    {
        public string timestamp;
        public string activeSceneName;
        public int totalRootGameObjects;
        public List<string> rootGameObjectNames;
        public bool isPlayMode;
        public float timeSinceStartup;
        public long allocatedMemoryBytes;
        public string allocatedMemoryMB;
        public string screenResolution;
        public string operatingSystem;
        public string graphicsDeviceName;
        public string unityVersion;
        public string issueTag;
        public string qaNotes;
    }

    private static RecorderController s_RecorderController;
    private static bool s_IsRecording = false;
    private static string s_CurrentSessionFolder = string.Empty;
    private static double s_StartTime = 0;
    private static int s_TargetFps = 30;
    private static string s_CurrentIssueTag = string.Empty;
    private static string s_CurrentNotes = string.Empty;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void ResetStaticsOnDomainReload()
    {
        s_RecorderController = null;
        s_IsRecording = false;
        s_CurrentSessionFolder = string.Empty;
        s_StartTime = 0;
        s_TargetFps = 30;
        s_CurrentIssueTag = string.Empty;
        s_CurrentNotes = string.Empty;
    }
#endif

    public static bool IsRecording => s_IsRecording && s_RecorderController != null && s_RecorderController.IsRecording();
    public static float ElapsedTime => s_IsRecording ? (float)(EditorApplication.timeSinceStartup - s_StartTime) : 0f;
    public static string CurrentSessionFolder => s_CurrentSessionFolder;

    public static bool IsRecorderAvailable()
    {
        return Type.GetType("UnityEditor.Recorder.RecorderController, Unity.Recorder.Editor") != null;
    }

    public static string GetRecordingRootDirectory()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string recordingDir = Path.Combine(projectRoot, "Recording");

        if (!Directory.Exists(recordingDir))
        {
            Directory.CreateDirectory(recordingDir);
        }

        return recordingDir;
    }

    public static bool StartRecording(int targetFps = 30, string issueTag = "", string notes = "")
    {
        if (IsRecording)
        {
            Debug.LogWarning("[HB_ScreenRecorder] 이미 녹화가 진행 중입니다.");
            return false;
        }

        s_TargetFps = Mathf.Clamp(targetFps, 10, 60);
        s_CurrentIssueTag = issueTag;
        s_CurrentNotes = notes;

        string rootDir = GetRecordingRootDirectory();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderName = string.IsNullOrEmpty(issueTag) 
            ? $"Record_{timestamp}" 
            : $"Record_{timestamp}_{SanitizeFileName(issueTag)}";

        s_CurrentSessionFolder = Path.Combine(rootDir, folderName);
        if (!Directory.Exists(s_CurrentSessionFolder))
        {
            Directory.CreateDirectory(s_CurrentSessionFolder);
        }

        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRatePlayback = FrameRatePlayback.Variable;
        controllerSettings.FrameRate = s_TargetFps;

        var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movieSettings.name = "HB_MovieRecorder";
        movieSettings.Enabled = true;
        movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        movieSettings.VideoBitRateMode = VideoBitrateMode.High;
        movieSettings.CaptureAudio = true;

        string videoFilePath = Path.Combine(s_CurrentSessionFolder, "recording");
        movieSettings.OutputFile = videoFilePath;

        movieSettings.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920,
            OutputHeight = 1080
        };

        controllerSettings.AddRecorderSettings(movieSettings);

        s_RecorderController = new RecorderController(controllerSettings);
        s_StartTime = EditorApplication.timeSinceStartup;

        s_RecorderController.PrepareRecording();
        s_RecorderController.StartRecording();
        s_IsRecording = true;

        Debug.Log($"[HB_ScreenRecorder] 녹화 시작: {s_CurrentSessionFolder} (Target FPS: {s_TargetFps})");
        return true;
    }

    public static void StopRecording()
    {
        if (!s_IsRecording) return;

        float duration = ElapsedTime;

        if (s_RecorderController != null)
        {
            s_RecorderController.StopRecording();
            s_RecorderController = null;
        }
        s_IsRecording = false;

        SaveDiagnosticLogsAndState(s_CurrentSessionFolder, "Video Recording Session", duration);
        Debug.Log($"[HB_ScreenRecorder] 녹화 완료 ({duration:F2}s): {s_CurrentSessionFolder}");
    }

    public static string TakeScreenshot(string issueTag = "", string notes = "")
    {
        string rootDir = GetRecordingRootDirectory();
        string screenshotsDir = Path.Combine(rootDir, "Screenshots");
        if (!Directory.Exists(screenshotsDir))
        {
            Directory.CreateDirectory(screenshotsDir);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string tagPart = string.IsNullOrEmpty(issueTag) ? "" : $"_{SanitizeFileName(issueTag)}";
        string baseFileName = $"Screenshot_{timestamp}{tagPart}";

        string imagePath = Path.Combine(screenshotsDir, $"{baseFileName}.png");

        ScreenCapture.CaptureScreenshot(imagePath);

        string infoPrefix = Path.Combine(screenshotsDir, baseFileName);
        SaveStateFile($"{infoPrefix}_State.json", issueTag, notes, 0f);
        SaveLogDumpFile($"{infoPrefix}_Logs.txt");

        Debug.Log($"[HB_ScreenRecorder] 스크린샷 저장 완료: {imagePath}");
        return imagePath;
    }

    private static void SaveDiagnosticLogsAndState(string targetFolder, string sessionType, float duration)
    {
        try
        {
            string statePath = Path.Combine(targetFolder, "Diagnostic_State.json");
            SaveStateFile(statePath, s_CurrentIssueTag, s_CurrentNotes, duration);

            string logPath = Path.Combine(targetFolder, "Diagnostic_Logs.txt");
            SaveLogDumpFile(logPath);

            string summaryPath = Path.Combine(targetFolder, "QA_BugReport_Summary.txt");
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           QA BUG REPORT & DIAGNOSTIC SUMMARY     ");
            sb.AppendLine("==================================================");
            sb.AppendLine($"Session Type    : {sessionType}");
            sb.AppendLine($"Session ID      : {Path.GetFileName(targetFolder)}");
            sb.AppendLine($"Issue Tag       : {s_CurrentIssueTag}");
            sb.AppendLine($"Date & Time     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration        : {duration:F2} seconds");
            sb.AppendLine($"Active Scene    : {SceneManager.GetActiveScene().name}");
            sb.AppendLine($"Play Mode       : {(Application.isPlaying ? "Active (Playing)" : "Edit Mode")}");
            sb.AppendLine($"Unity Version   : {Application.unityVersion}");
            sb.AppendLine($"OS Platform     : {SystemInfo.operatingSystem}");
            sb.AppendLine($"Resolution      : {Screen.width}x{Screen.height}");
            sb.AppendLine($"QA Notes        : {s_CurrentNotes}");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("Saved Files:");
            sb.AppendLine("- Video           : recording.mp4");
            sb.AppendLine("- Diagnostics Log : Diagnostic_Logs.txt");
            sb.AppendLine("- System State    : Diagnostic_State.json");
            sb.AppendLine("==================================================");

            File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HB_ScreenRecorder] 진단 데이터 저장 실패: {ex.Message}");
        }
    }

    private static void SaveStateFile(string filePath, string issueTag, string notes, float duration)
    {
        var activeScene = SceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        var rootNames = new List<string>();
        foreach (var r in roots)
        {
            rootNames.Add(r.name);
        }

        long memBytes = GC.GetTotalMemory(false);
        var state = new HB_DiagnosticState
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            activeSceneName = activeScene.name,
            totalRootGameObjects = roots.Length,
            rootGameObjectNames = rootNames,
            isPlayMode = Application.isPlaying,
            timeSinceStartup = (float)Math.Round(EditorApplication.timeSinceStartup, 2),
            allocatedMemoryBytes = memBytes,
            allocatedMemoryMB = $"{memBytes / (1024f * 1024f):F2} MB",
            screenResolution = $"{Screen.width}x{Screen.height}",
            operatingSystem = SystemInfo.operatingSystem,
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            unityVersion = Application.unityVersion,
            issueTag = issueTag,
            qaNotes = notes
        };

        string json = EditorJsonUtility.ToJson(state, true);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    private static void SaveLogDumpFile(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DIAGNOSTIC CONSOLE LOG DUMP ===");
        sb.AppendLine($"Dump Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("--------------------------------------------------");

        Type hbLogType = Type.GetType("InGameConsoleSystem.HLogCore, HB.InGameConsole")
                      ?? Type.GetType("InGameConsoleSystem.HLogCore")
                      ?? Type.GetType("HLog.HLogCore, HB.InGameConsole")
                      ?? Type.GetType("HLog.HLogCore")
                      ?? Type.GetType("HLog, HB.InGameConsole")
                      ?? Type.GetType("HLog, Assembly-CSharp")
                      ?? Type.GetType("HLog")
                      ?? Type.GetType("Logger, HB.InGameConsole") 
                      ?? Type.GetType("Logger, Assembly-CSharp") 
                      ?? Type.GetType("Logger")
                      ?? Type.GetType("HB_Log, HB.InGameConsole") 
                      ?? Type.GetType("HB_Log, Assembly-CSharp") 
                      ?? Type.GetType("HB_Log");

        if (hbLogType != null)
        {
            PropertyInfo historyProp = hbLogType.GetProperty("History", BindingFlags.Public | BindingFlags.Static);
            if (historyProp != null && historyProp.GetValue(null) is IEnumerable historyEnumerable)
            {
                var historyList = new List<object>();
                foreach (var item in historyEnumerable)
                {
                    historyList.Add(item);
                }

                int errorCount = 0;
                int warnCount = 0;

                for (int i = 0; i < historyList.Count; i++)
                {
                    var item = historyList[i];
                    PropertyInfo logTypeProp = item.GetType().GetProperty("LogType");
                    if (logTypeProp != null)
                    {
                        var lt = (LogType)logTypeProp.GetValue(item);
                        if (lt == LogType.Error || lt == LogType.Exception) errorCount++;
                        else if (lt == LogType.Warning) warnCount++;
                    }
                }

                sb.AppendLine($"Total Logs: {historyList.Count} (Errors: {errorCount}, Warnings: {warnCount})\n");

                for (int i = 0; i < historyList.Count; i++)
                {
                    var item = historyList[i];
                    var itemType = item.GetType();
                    string timeString = itemType.GetProperty("TimeString")?.GetValue(item)?.ToString() ?? "";
                    string logTypeStr = itemType.GetProperty("LogType")?.GetValue(item)?.ToString() ?? "";
                    string message = itemType.GetProperty("Message")?.GetValue(item)?.ToString() ?? "";
                    string stackTrace = itemType.GetProperty("StackTrace")?.GetValue(item)?.ToString() ?? "";

                    sb.AppendLine($"[{timeString}] [{logTypeStr}] {message}");
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        sb.AppendLine("Stack Trace:\n" + stackTrace.Trim());
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                return;
            }
        }

        sb.AppendLine("No HLog/Logger/HB_Log instance found or InGameConsole is not installed.");
        sb.AppendLine("Standalone mode: Recording and Screenshot captured successfully.");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            path = GetRecordingRootDirectory();
        }

        EditorUtility.RevealInFinder(path);
    }
}
