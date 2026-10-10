#pragma warning disable UAL0010, UAL0013

using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// Unity Recorder 기반 동영상 녹화, 스크린샷 및 시스템 진단(사양/로그 분석) 에디터 윈도우입니다.
/// QA 및 현업 아티스트 및 개발 이슈 상황을 원클릭으로 녹화하고 메타데이터와 함께 저장합니다.
/// Windows 및 macOS 양방향을 모두 지원하며, 패키지 미설치 시 안전한 안내와 설치를 제공합니다.
/// </summary>
public class HB_ScreenRecorderWindow : EditorWindow
{
    private int targetFps = 30;
    private string issueTag = "BUG_101";
    private string qaNotes = "인게임 플레이 테스트 중 재현 현상 기록";
    private Vector2 scrollPosition;

    private static AddRequest s_InstallRequest;

    [MenuItem("Tools/HB/Screen Recorder")]
    public static void ShowWindow()
    {
        var window = GetWindow<HB_ScreenRecorderWindow>("HB Recorder");
        window.minSize = new Vector2(380, 560);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        // 녹화 중 타이머 갱신을 위한 Repaint
        if (HB_ScreenRecorder.IsRecording)
        {
            Repaint();
        }

        // 패키지 설치 비동기 완료 모니터링
        if (s_InstallRequest != null && s_InstallRequest.IsCompleted)
        {
            if (s_InstallRequest.Status == StatusCode.Success)
            {
                Debug.Log("[HB_ScreenRecorder] Unity Recorder 패키지 설치가 완료되었습니다.");
                EditorUtility.DisplayDialog("설치 완료", "Unity Recorder 패키지가 성공적으로 설치되었습니다!", "확인");
            }
            else
            {
                Debug.LogError($"[HB_ScreenRecorder] 패키지 설치 실패: {s_InstallRequest.Error?.message}");
                EditorUtility.DisplayDialog("설치 실패", $"패키지 설치 중 오류가 발생했습니다:\n{s_InstallRequest.Error?.message}", "확인");
            }
            s_InstallRequest = null;
            Repaint();
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawHeader();
        EditorGUILayout.Space(8);

        DrawPackageStatusBanner();
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
        GUILayout.Label("🎥 HB Screen Recorder (Unity Recorder & QA Tool)", EditorStyles.boldLabel);
        GUILayout.Label("원클릭 고화질 무손실 MP4 녹화, 스크린샷 및 실시간 메모리/사양 분석 진단 도구", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawPackageStatusBanner()
    {
        bool hasRecorder = HB_ScreenRecorder.IsRecorderAvailable();

        if (hasRecorder)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            GUILayout.Label("✔ Unity Recorder Engine Active (MP4 고화질 녹화 지원)", EditorStyles.miniBoldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = new Color(1f, 0.4f, 0.4f);
            GUILayout.Label("⚠️ Unity Recorder (com.unity.recorder) 미설치", EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.LabelField("고화질 MP4 녹화 기능을 사용하려면 Unity Recorder 패키지가 필요합니다.", EditorStyles.wordWrappedMiniLabel);

            if (s_InstallRequest != null)
            {
                EditorGUILayout.HelpBox("Unity Recorder 패키지 설치 진행 중... 잠시 기다려주세요.", MessageType.Info);
            }
            else
            {
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
                if (GUILayout.Button("📥 Unity Recorder 패키지 원클릭 자동 설치", GUILayout.Height(30)))
                {
                    PromptInstallRecorderPackage();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndVertical();
        }
    }

    private void PromptInstallRecorderPackage()
    {
        bool confirm = EditorUtility.DisplayDialog(
            "Unity Recorder 패키지 설치",
            "유니티 공식 화면 녹화 패키지(com.unity.recorder)를 설치하시겠습니까?\n\n설치 시 MP4 고화질 영상 녹화 기능을 사용할 수 있습니다.",
            "지금 설치",
            "취소"
        );

        if (confirm)
        {
            Debug.Log("[HB_ScreenRecorder] com.unity.recorder 패키지 설치를 시작합니다...");
            s_InstallRequest = Client.Add("com.unity.recorder");
        }
    }

    private void DrawRecordingSection()
    {
        bool isRec = HB_ScreenRecorder.IsRecording;

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🎬 화면 녹화 (Video Recording)", EditorStyles.boldLabel);

        if (isRec)
        {
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            float elapsed = HB_ScreenRecorder.ElapsedTime;
            int min = (int)(elapsed / 60);
            int sec = (int)(elapsed % 60);
            GUILayout.Label($"🔴 REC [{min:D2}:{sec:D2}] - MP4 녹화 진행 중...", EditorStyles.boldLabel);
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUILayout.Label("⚪ Standby (대기 중)", EditorStyles.label);
        }

        EditorGUILayout.Space(6);

        if (!isRec)
        {
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            if (GUILayout.Button("▶ Start MP4 Recording", GUILayout.Height(38)))
            {
                if (!HB_ScreenRecorder.IsRecorderAvailable())
                {
                    PromptInstallRecorderPackage();
                }
                else
                {
                    HB_ScreenRecorder.StartRecording(targetFps, issueTag, qaNotes);
                }
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.backgroundColor = new Color(0.95f, 0.3f, 0.3f);
            if (GUILayout.Button("⏹ Stop Recording & Save Diagnostics", GUILayout.Height(38)))
            {
                HB_ScreenRecorder.StopRecording();
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.HelpBox("녹화 종료 시 영상파일(MP4), 실행 사양(Diagnostic_Logs.txt), 씬/메모리 상태(Diagnostic_State.json)가 자동 저장됩니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void DrawScreenshotSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📸 스크린샷 캡처 (Instant Screenshot)", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.35f, 0.7f, 1f);
        if (GUILayout.Button("📷 Take Screenshot & Capture Diagnostics", GUILayout.Height(32)))
        {
            string savedPath = HB_ScreenRecorder.TakeScreenshot(issueTag, qaNotes);
            ShowNotification(new GUIContent($"Screenshot Saved!\n{Path.GetFileName(savedPath)}"));
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox("스크린샷 캡처 시 현재 화면 캡처본과 함께 실시간 사양 및 사양 로그 정보가 함께 영구 저장됩니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void DrawSettingsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("⚙️ QA 레포트 메타데이터 입력", EditorStyles.boldLabel);

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
        GUILayout.Label("📁 저장 경로 (Project Root / Recording)", EditorStyles.boldLabel);

        string rootDir = HB_ScreenRecorder.GetRecordingRootDirectory();
        EditorGUILayout.SelectableLabel(rootDir, EditorStyles.textField, GUILayout.Height(20));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📂 Open Recording Root Folder", GUILayout.Height(26)))
        {
            HB_ScreenRecorder.OpenFolder(rootDir);
        }

        string screenshotsDir = Path.Combine(rootDir, "Screenshots");
        if (GUILayout.Button("🖼️ Open Screenshots", GUILayout.Height(26)))
        {
            HB_ScreenRecorder.OpenFolder(screenshotsDir);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawRecentSessions()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📋 최근 녹화 세션 히스토리", EditorStyles.boldLabel);

        string rootDir = HB_ScreenRecorder.GetRecordingRootDirectory();
        if (Directory.Exists(rootDir))
        {
            var directories = Directory.GetDirectories(rootDir);
            Array.Sort(directories, (a, b) => Directory.GetCreationTime(b).CompareTo(Directory.GetCreationTime(a)));

            int shown = 0;
            for (int i = 0; i < directories.Length; i++)
            {
                string dir = directories[i];
                string dirName = Path.GetFileName(dir);
                if (dirName.Equals("Screenshots", StringComparison.OrdinalIgnoreCase)) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(dirName, EditorStyles.miniLabel);

                if (GUILayout.Button("폴더 열기", GUILayout.Width(70)))
                {
                    HB_ScreenRecorder.OpenFolder(dir);
                }
                EditorGUILayout.EndHorizontal();

                shown++;
                if (shown >= 5) break;
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField("녹화된 세션이 없습니다.", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.EndVertical();
    }
}
