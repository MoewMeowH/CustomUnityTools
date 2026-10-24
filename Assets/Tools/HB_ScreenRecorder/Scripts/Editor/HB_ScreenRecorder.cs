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

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
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
        /// <summary> 녹화 길이(초). 스크린샷은 0 </summary>
        public float recordingDurationSeconds;
        /// <summary> 관리 힙 크기 (GC.GetTotalMemory, C# 객체만) </summary>
        public long managedHeapBytes;
        public string managedHeapMB;
        /// <summary> Unity 네이티브 할당 (Profiler.GetTotalAllocatedMemoryLong — Unity 내부 할당자, 관리 힙은 들어가지 않음) </summary>
        public long unityAllocatedBytes;
        public string unityAllocatedMB;
        /// <summary> Game 뷰 렌더 해상도 (에디터 창 크기가 아님) </summary>
        public string screenResolution;
        public string operatingSystem;
        public string graphicsDeviceName;
        public string unityVersion;
        public string issueTag;
        public string qaNotes;
    }

    // 영상 출력 해상도 (Game 뷰 크기와 무관하게 고정)
    private const int VideoOutputWidth = 1920;
    private const int VideoOutputHeight = 1080;

    private static RecorderController s_RecorderController;
    private static RecorderControllerSettings s_ControllerSettings;
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
        s_ControllerSettings = null;
        s_IsRecording = false;
        s_CurrentSessionFolder = string.Empty;
        s_StartTime = 0;
        s_TargetFps = 30;
        s_CurrentIssueTag = string.Empty;
        s_CurrentNotes = string.Empty;

        // Recorder는 Play Mode에서만 녹화되므로, Play Mode를 나가면 자동으로 녹화를 마무리하고 진단 파일을 저장한다
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // Recorder가 스스로 멈추면 그때 바로 마무리한다 (다음 시작 · Play 종료까지 미루면 녹화 길이 · 상태가 늦은 시점 값이 된다)
        EditorApplication.update -= FinalizeIfRecorderStopped;
        EditorApplication.update += FinalizeIfRecorderStopped;

        // 녹화 중 스크립트 재컴파일이면 정적 상태가 사라지기 전에 마무리한다 (MP4 마무리 · 진단 저장 · 설정 SO 파괴)
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && s_IsRecording)
        {
            Debug.Log("[HB_ScreenRecorder] Play Mode 종료로 녹화를 자동 저장합니다.");
            StopRecording();
        }
    }

    private static void FinalizeIfRecorderStopped()
    {
        if (s_IsRecording && !IsRecording)
        {
            Debug.LogWarning("[HB_ScreenRecorder] Recorder가 녹화를 멈춰 세션을 마무리합니다.");
            StopRecording();
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        if (s_IsRecording)
        {
            Debug.Log("[HB_ScreenRecorder] 스크립트 재컴파일로 녹화를 자동 저장합니다.");
            StopRecording();
        }
    }
#endif

    public static bool IsRecording => s_IsRecording && s_RecorderController != null && s_RecorderController.IsRecording();
    public static float ElapsedTime => s_IsRecording ? (float)(EditorApplication.timeSinceStartup - s_StartTime) : 0f;
    public static string CurrentSessionFolder => s_CurrentSessionFolder;

    /// <summary> 저장 루트 경로 (프로젝트 루트/Recording). 폴더를 만들지 않는다 — 창 표시처럼 자주 부르는 곳용 </summary>
    public static string GetRecordingRootPath()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Recording");
    }

    /// <summary> 저장 루트 경로. 없으면 만든다 (저장할 때용) </summary>
    public static string GetRecordingRootDirectory()
    {
        string recordingDir = GetRecordingRootPath();
        if (!Directory.Exists(recordingDir))
        {
            Directory.CreateDirectory(recordingDir);
        }
        return recordingDir;
    }

    /// <summary> Game 뷰 렌더 해상도. 에디터 창 버튼(OnGUI)에서 Screen.width/height는 그 창 크기라 쓰지 않는다 </summary>
    public static string GetGameViewResolution()
    {
        Vector2 size = Handles.GetMainGameViewSize();
        return $"{(int)size.x}x{(int)size.y}";
    }

    public static bool StartRecording(int targetFps = 30, string issueTag = "", string notes = "")
    {
        if (IsRecording)
        {
            Debug.LogWarning("[HB_ScreenRecorder] 이미 녹화가 진행 중입니다.");
            return false;
        }

        // Recorder가 스스로 멈췄는데(IsRecording false) 이쪽 상태가 남아 있으면, 이전 세션을 마무리하고 설정 SO를 정리한 뒤 새로 시작한다
        // (보통은 FinalizeIfRecorderStopped가 먼저 마무리한다 — 같은 프레임에 다시 시작하는 경우의 안전망)
        if (s_IsRecording)
        {
            Debug.LogWarning("[HB_ScreenRecorder] 이전 녹화가 Recorder에서 이미 멈춰 있어 먼저 마무리합니다.");
            StopRecording();
        }

        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[HB_ScreenRecorder] Unity Recorder는 Play Mode에서만 녹화할 수 있습니다. Play Mode에 들어간 뒤 시작하세요.");
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
            OutputWidth = VideoOutputWidth,
            OutputHeight = VideoOutputHeight
        };

        controllerSettings.AddRecorderSettings(movieSettings);

        s_ControllerSettings = controllerSettings;
        try
        {
            s_RecorderController = new RecorderController(controllerSettings);
            s_StartTime = EditorApplication.timeSinceStartup;

            s_RecorderController.PrepareRecording();
            s_RecorderController.StartRecording();
        }
        catch (Exception ex)
        {
            // 시작에 실패하면 이번에 만든 설정 SO가 남지 않게 정리한다
            Debug.LogError($"[HB_ScreenRecorder] 녹화 시작 실패: {ex.Message}");
            s_RecorderController = null;
            DestroyControllerSettings();
            return false;
        }
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
        DestroyControllerSettings();

        SaveDiagnosticLogsAndState(s_CurrentSessionFolder, "Video Recording Session", duration);
        Debug.Log($"[HB_ScreenRecorder] 녹화 완료 ({duration:F2}s): {s_CurrentSessionFolder}");
    }

    /// <summary>
    /// 녹화마다 만드는 설정 ScriptableObject(컨트롤러 + 무비 레코더 설정)를 정리합니다.
    /// </summary>
    private static void DestroyControllerSettings()
    {
        if (s_ControllerSettings == null) return;

        foreach (var recorder in s_ControllerSettings.RecorderSettings)
        {
            if (recorder != null)
                UnityEngine.Object.DestroyImmediate(recorder);
        }
        UnityEngine.Object.DestroyImmediate(s_ControllerSettings);
        s_ControllerSettings = null;
    }

    /// <summary> 스크린샷을 요청하고 상태 · 로그 파일을 저장합니다. PNG 경로를 반환하고, 폴더를 만들지 못하면 null </summary>
    public static string TakeScreenshot(string issueTag = "", string notes = "")
    {
        string screenshotsDir;
        try
        {
            screenshotsDir = Path.Combine(GetRecordingRootDirectory(), "Screenshots");
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HB_ScreenRecorder] 스크린샷 폴더를 만들지 못했습니다: {ex.Message}");
            return null;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string tagPart = string.IsNullOrEmpty(issueTag) ? "" : $"_{SanitizeFileName(issueTag)}";
        string baseFileName = $"Screenshot_{timestamp}{tagPart}";

        // 같은 초에 다시 찍으면 이름이 겹친다 → 이전 파일을 덮어쓰지 않고, 창이 이전 PNG를 보고 "저장됨"으로 오판하지 않게 번호를 붙인다
        // (상태 JSON은 바로 쓰므로, PNG가 아직 안 생긴 직전 요청도 이것으로 걸러진다)
        for (int n = 1; File.Exists(Path.Combine(screenshotsDir, $"{baseFileName}.png")) || File.Exists(Path.Combine(screenshotsDir, $"{baseFileName}_State.json")); n++)
        {
            baseFileName = $"Screenshot_{timestamp}{tagPart}_{n}";
        }

        string imagePath = Path.Combine(screenshotsDir, $"{baseFileName}.png");

        ScreenCapture.CaptureScreenshot(imagePath);

        // 캡처는 Game 뷰가 다음에 그릴 때 실행된다. 창 버튼만 누른 Edit Mode에서는 다시 그릴 계기가 없으므로 직접 요청한다
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.QueuePlayerLoopUpdate();
        }
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        // 상태 · 로그 저장 실패(IO)가 창 버튼까지 올라가지 않게 한다 (이미지는 이미 요청됨)
        string infoPrefix = Path.Combine(screenshotsDir, baseFileName);
        try
        {
            SaveStateFile($"{infoPrefix}_State.json", issueTag, notes, 0f);
            SaveLogDumpFile($"{infoPrefix}_Logs.txt");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HB_ScreenRecorder] 스크린샷 진단 데이터 저장 실패: {ex.Message}");
        }

        // CaptureScreenshot은 Game 뷰가 다음에 그릴 때 파일을 쓴다 → 여기서는 요청만 (창이 파일이 생기는지 확인해 알림)
        Debug.Log($"[HB_ScreenRecorder] 스크린샷 요청: {imagePath}");
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
            sb.AppendLine($"Video Output    : {VideoOutputWidth}x{VideoOutputHeight} (MP4)");
            sb.AppendLine($"Resolution      : {GetGameViewResolution()} (Game view)");
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

        long managedBytes = GC.GetTotalMemory(false);
        long unityBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        var state = new HB_DiagnosticState
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            activeSceneName = activeScene.name,
            totalRootGameObjects = roots.Length,
            rootGameObjectNames = rootNames,
            isPlayMode = Application.isPlaying,
            timeSinceStartup = (float)Math.Round(EditorApplication.timeSinceStartup, 2),
            recordingDurationSeconds = (float)Math.Round(duration, 2),
            managedHeapBytes = managedBytes,
            managedHeapMB = $"{managedBytes / (1024f * 1024f):F2} MB",
            unityAllocatedBytes = unityBytes,
            unityAllocatedMB = $"{unityBytes / (1024f * 1024f):F2} MB",
            screenResolution = GetGameViewResolution(),
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

        // HB_InGameConsole 패키지가 설치되어 있으면 로그 기록을 가져온다 (선택 의존이라 리플렉션으로 조회)
        Type hbLogType = Type.GetType("HLogCore, HB.InGameConsole");

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
