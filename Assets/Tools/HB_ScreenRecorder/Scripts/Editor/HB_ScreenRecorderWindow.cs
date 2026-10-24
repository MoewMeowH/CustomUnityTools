using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public class HB_ScreenRecorderWindow : EditorWindow
{
    // 도메인 리로드 · 창 레이아웃 저장 후에도 입력값이 남도록 직렬화한다
    [SerializeField] private int targetFps = 30;
    [SerializeField] private string issueTag = "BUG_101";
    [SerializeField] private string qaNotes = "인게임 플레이 테스트 중 재현 현상 기록";
    private Vector2 scrollPosition;

    // 스크린샷은 Game 뷰가 다음에 그릴 때 저장된다 → 파일이 생길 때까지 확인한 뒤 알린다.
    // 도메인 리로드는 창의 private 필드도 되살리며 null 문자열을 ""로 돌려주므로 리로드 대상에서 뺀다 (빈 경로로 "저장 안 됨" 경고가 나지 않게)
    private const double ScreenshotWaitSeconds = 5.0;
    [NonSerialized] private string pendingScreenshotPath;
    [NonSerialized] private double pendingScreenshotDeadline;

    // 최근 세션 목록은 디스크를 읽으므로 그릴 때마다가 아니라 바뀔 만한 때만 다시 읽는다
    private const int RecentSessionCount = 5;
    private readonly List<string> recentSessions = new List<string>();
    private bool isRecentSessionsDirty = true;
    private bool wasRecording;

    [MenuItem("Tools/HB/Screen Recorder")]
    public static void ShowWindow()
    {
        var window = GetWindow<HB_ScreenRecorderWindow>("HB Recorder");
        window.minSize = new Vector2(380, 520);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        isRecentSessionsDirty = true;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnFocus()
    {
        // 다른 곳(탐색기 등)에서 폴더를 지웠을 수 있다
        isRecentSessionsDirty = true;
    }

    private void OnEditorUpdate()
    {
        bool isRecording = HB_ScreenRecorder.IsRecording;
        if (isRecording)
        {
            Repaint();
        }

        // 녹화가 끝나면(정지 버튼 · Play Mode 종료 · Recorder가 스스로 멈춤) 새 세션 폴더가 생겼으므로 목록을 다시 읽는다
        if (wasRecording != isRecording)
        {
            wasRecording = isRecording;
            isRecentSessionsDirty = true;
            Repaint();
        }

        if (!string.IsNullOrEmpty(pendingScreenshotPath))
        {
            if (File.Exists(pendingScreenshotPath))
            {
                ShowNotification(new GUIContent($"Screenshot Saved: {Path.GetFileName(pendingScreenshotPath)}"));
                pendingScreenshotPath = null;
            }
            else if (EditorApplication.timeSinceStartup > pendingScreenshotDeadline)
            {
                // 캡처 요청은 취소되지 않는다 → Game 뷰가 나중에 그려지면 그 시점 화면이 이 이름으로 저장된다
                ShowNotification(new GUIContent("Screenshot not saved yet (waiting for Game view)"));
                Debug.LogWarning($"[HB_ScreenRecorder] 스크린샷이 {ScreenshotWaitSeconds:F0}초 안에 저장되지 않았습니다. 요청은 남아 있어 Game 뷰가 그려질 때 그 시점 화면으로 저장됩니다(상태 · 로그 파일은 요청 시점). Game 뷰 탭이 보이는지 확인하세요: {pendingScreenshotPath}");
                pendingScreenshotPath = null;
            }
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawHeader();
        EditorGUILayout.Space(10);

        DrawRecordingSection();
        EditorGUILayout.Space(12);

        DrawScreenshotSection();
        EditorGUILayout.Space(12);

        DrawSettingsSection();
        EditorGUILayout.Space(12);

        DrawStorageSection();
        EditorGUILayout.Space(12);

        DrawRecentSessions();

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("HB Screen Recorder (Unity Recorder & QA Tool)", EditorStyles.boldLabel);
        GUILayout.Label("MP4 녹화, 스크린샷 및 런타임 상태/로그 덤프 도구", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawRecordingSection()
    {
        bool isRec = HB_ScreenRecorder.IsRecording;

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("화면 녹화 (Video Recording)", EditorStyles.boldLabel);

        if (isRec)
        {
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            float elapsed = HB_ScreenRecorder.ElapsedTime;
            int min = (int)(elapsed / 60);
            int sec = (int)(elapsed % 60);
            GUILayout.Label($"[REC {min:D2}:{sec:D2}] MP4 녹화 진행 중...", EditorStyles.boldLabel);
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUILayout.Label("Standby (대기 중)", EditorStyles.label);
        }

        EditorGUILayout.Space(6);

        if (!isRec)
        {
            bool canRecord = EditorApplication.isPlaying;
            if (!canRecord)
            {
                EditorGUILayout.HelpBox("Unity Recorder는 Play Mode에서만 녹화됩니다. Play Mode에 들어간 뒤 시작하세요.", MessageType.Info);
            }

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            using (new EditorGUI.DisabledScope(!canRecord))
            if (GUILayout.Button("Start MP4 Recording", GUILayout.Height(38)))
            {
                HB_ScreenRecorder.StartRecording(targetFps, issueTag, qaNotes);
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.backgroundColor = new Color(0.95f, 0.3f, 0.3f);
            if (GUILayout.Button("Stop Recording & Save Diagnostics", GUILayout.Height(38)))
            {
                HB_ScreenRecorder.StopRecording();
                isRecentSessionsDirty = true;
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.HelpBox("녹화 종료 시 영상파일(MP4), 콘솔 로그(Diagnostic_Logs.txt), 씬/메모리 상태(Diagnostic_State.json)가 함께 저장됩니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void DrawScreenshotSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("스크린샷 캡처 (Screenshot)", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.35f, 0.7f, 1f);
        if (GUILayout.Button("Take Screenshot & Capture Diagnostics", GUILayout.Height(32)))
        {
            pendingScreenshotPath = HB_ScreenRecorder.TakeScreenshot(issueTag, qaNotes);
            pendingScreenshotDeadline = EditorApplication.timeSinceStartup + ScreenshotWaitSeconds;
            ShowNotification(new GUIContent(pendingScreenshotPath != null ? "Screenshot requested..." : "Screenshot failed (see Console)"));
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox("스크린샷 캡처 시 화면 이미지와 당시의 상태/로그 파일이 함께 저장됩니다. 이미지는 Game 뷰가 다음에 그릴 때 저장됩니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void DrawSettingsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("QA 리포트 메타데이터 설정", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(HB_ScreenRecorder.IsRecording);

        issueTag = EditorGUILayout.TextField("Issue / Bug Tag", issueTag);
        targetFps = EditorGUILayout.IntSlider("Target FPS", targetFps, 10, 60);

        EditorGUILayout.LabelField("QA Notes / 재현 상황 메모");
        qaNotes = EditorGUILayout.TextArea(qaNotes, GUILayout.Height(45));

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();
    }

    private void DrawStorageSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("저장 경로 (Project Root / Recording)", EditorStyles.boldLabel);

        // 그릴 때마다 폴더를 만들지 않는다 (경로만 계산, 여는 버튼에서 만든다)
        string rootDir = HB_ScreenRecorder.GetRecordingRootPath();
        EditorGUILayout.SelectableLabel(rootDir, EditorStyles.textField, GUILayout.Height(20));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Recording Folder", GUILayout.Height(26)))
        {
            HB_ScreenRecorder.OpenFolder(rootDir);
        }

        string screenshotsDir = Path.Combine(rootDir, "Screenshots");
        if (GUILayout.Button("Open Screenshots", GUILayout.Height(26)))
        {
            HB_ScreenRecorder.OpenFolder(screenshotsDir);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void RefreshRecentSessions()
    {
        isRecentSessionsDirty = false;
        recentSessions.Clear();

        string rootDir = HB_ScreenRecorder.GetRecordingRootPath();
        if (!Directory.Exists(rootDir)) return;

        try
        {
            var directories = Directory.GetDirectories(rootDir);
            Array.Sort(directories, (a, b) => Directory.GetCreationTime(b).CompareTo(Directory.GetCreationTime(a)));
            foreach (string dir in directories)
            {
                if (Path.GetFileName(dir).Equals("Screenshots", StringComparison.OrdinalIgnoreCase)) continue;
                recentSessions.Add(dir);
                if (recentSessions.Count >= RecentSessionCount) break;
            }
        }
        catch (Exception ex) // IOException · UnauthorizedAccessException · 정렬 비교자 예외(InvalidOperationException으로 감싸짐)
        {
            Debug.LogWarning($"[HB_ScreenRecorder] 최근 세션 목록을 읽지 못했습니다: {ex.Message}");
        }
    }

    private void DrawRecentSessions()
    {
        // 목록 개수가 바뀌면 컨트롤 수가 달라지므로, 버튼 이벤트 중이 아니라 다음 Layout 패스에서만 다시 읽는다
        if (isRecentSessionsDirty && Event.current.type == EventType.Layout) RefreshRecentSessions();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("최근 녹화 세션 히스토리", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("새로고침", GUILayout.Width(70)))
        {
            isRecentSessionsDirty = true;
        }
        EditorGUILayout.EndHorizontal();

        foreach (string dir in recentSessions)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Path.GetFileName(dir), EditorStyles.miniLabel);

            if (GUILayout.Button("폴더 열기", GUILayout.Width(70)))
            {
                HB_ScreenRecorder.OpenFolder(dir);
            }
            EditorGUILayout.EndHorizontal();
        }

        if (recentSessions.Count == 0)
        {
            EditorGUILayout.LabelField("녹화된 세션이 없습니다.", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }
}
