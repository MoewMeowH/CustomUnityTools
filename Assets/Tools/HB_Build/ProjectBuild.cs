using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity 커스텀 빌드 매니저 및 Jenkins CI/CD 연동 에디터 윈도우입니다.
/// 조건부 파이프라인 스텝 필터링, 체크박스 On/Off, 스텝 행 클릭 시 인라인 C# 소스 코드 미리보기,
/// 선택적 SceneDefine/ProjectDefine 에셋 관리 및 Jenkinsfile 생성을 지원합니다.
/// </summary>
public class ProjectBuild : EditorWindow
{
    public enum ESelfBuildPlatform
    {
        PC = 0,
        Android = 1,
        iOS = 2,
    }

    [SerializeField] private ESelfBuildPlatform m_Platform = ESelfBuildPlatform.PC;
    [SerializeField] private EBuildType m_BuildType = EBuildType.DEV;
    [SerializeField] private string m_Version = "1.0.0";
    [SerializeField] private string m_VersionCode = "1";
    [SerializeField] private bool m_DevBuild = false;
    [SerializeField] private bool m_DeepProfile = false;
    [SerializeField] private bool m_CleanBuild = false;
    [SerializeField] private bool m_IsAab = false;
    [SerializeField] private bool m_ZipPackage = false;
    [SerializeField] private string m_ProvisionUUID = string.Empty;
    [SerializeField] private string m_OutputPath = string.Empty;
    [SerializeField] private List<string> m_ScenePath = new List<string>();

    // 선택적 설정 에셋 (미할당 시 자동 건너뜀)
    [SerializeField] private SceneDefine m_SceneDefine;
    [SerializeField] private ProjectDefine m_ProjectDefine;

    private Vector2 m_ScrollPosition;
    private bool m_ShowPipelineSteps = true;
    private bool m_ShowOnlyActiveSteps = true;

    // 코드 미리보기 상태
    private string m_PreviewStepId = string.Empty;
    private string m_PreviewStepName = string.Empty;
    private string m_PreviewScriptPath = string.Empty;
    private string m_PreviewClassSource = string.Empty;
    private string m_PreviewFullSource = string.Empty;
    private int m_PreviewLineNumber = 1;
    private bool m_ShowOnlyClassBlock = true;
    private Vector2 m_CodePreviewScroll;
    private static GUIStyle s_CodePreviewStyle;

    private static GUIStyle CodePreviewStyle
    {
        get
        {
            if (s_CodePreviewStyle == null)
            {
                s_CodePreviewStyle = new GUIStyle(EditorStyles.textArea)
                {
                    font = EditorStyles.standardFont,
                    fontSize = 12,
                    wordWrap = false
                };
            }
            return s_CodePreviewStyle;
        }
    }

    [MenuItem("Tools/HB/Custom Build Manager")]
    public static void ShowWindow()
    {
        var window = GetWindow<ProjectBuild>("HB Build Manager");
        window.minSize = new Vector2(480, 750);
        window.Show();
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(m_Version))
            m_Version = Application.version;

        if (string.IsNullOrEmpty(m_VersionCode))
            m_VersionCode = PlayerSettings.Android.bundleVersionCode.ToString();

        if (string.IsNullOrEmpty(m_OutputPath))
            m_OutputPath = BuildHelper.GetDefaultOutputPath(GetBuildTarget(m_Platform));

        if (m_ScenePath.Count == 0)
            LoadScenesFromBuildSettings();

        if (m_SceneDefine == null)
            m_SceneDefine = SceneDefine.LoadIfExists();

        if (m_ProjectDefine == null)
            m_ProjectDefine = ProjectDefine.LoadIfExists();

        BuildStepRegistry.ReloadSteps();
    }

    private void OnGUI()
    {
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

        DrawHeader();
        EditorGUILayout.Space(8);

        DrawPlatformSection();
        EditorGUILayout.Space(8);

        DrawOptionalAssetsSection();
        EditorGUILayout.Space(8);

        DrawPipelineStepsSequence();
        EditorGUILayout.Space(8);

        DrawVersionSection();
        EditorGUILayout.Space(8);

        DrawOptionsSection();
        EditorGUILayout.Space(8);

        DrawPathSection();
        EditorGUILayout.Space(8);

        DrawSceneSection();
        EditorGUILayout.Space(14);

        DrawActionButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("🛠️ HB Custom Build Manager", EditorStyles.boldLabel);
        GUILayout.Label("모듈형 파이프라인(IPreBuildStep/IPostBuildStep) 및 Jenkins Groovy 지원 도구", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawPlatformSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📌 타겟 플랫폼 & 환경", EditorStyles.boldLabel);

        var prevPlatform = m_Platform;
        m_Platform = (ESelfBuildPlatform)EditorGUILayout.EnumPopup("Target Platform", m_Platform);
        if (prevPlatform != m_Platform && string.IsNullOrEmpty(m_OutputPath))
        {
            m_OutputPath = BuildHelper.GetDefaultOutputPath(GetBuildTarget(m_Platform));
        }

        m_BuildType = (EBuildType)EditorGUILayout.EnumPopup("Build Type", m_BuildType);
        EditorGUILayout.EndVertical();
    }

    private void DrawOptionalAssetsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📦 선택적 프로젝트 설정 에셋 (Optional Build Configs)", EditorStyles.boldLabel);

        // 1. SceneDefine 에셋 슬롯
        EditorGUILayout.BeginHorizontal();
        m_SceneDefine = (SceneDefine)EditorGUILayout.ObjectField("Scene Define", m_SceneDefine, typeof(SceneDefine), false);
        if (m_SceneDefine == null)
        {
            if (GUILayout.Button("+ 에셋 생성", GUILayout.Width(80)))
            {
                m_SceneDefine = SceneDefine.CreateAssetInUserFolder();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(m_SceneDefine == null ? "  ↳ 미생성/미할당 시: 등록된 모든 씬 빌드 (건너뛰기)" : "  ↳ 할당됨: 빌드 환경(DEV/QA/LIVE)별 IgnoreScene 자동 제외", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);

        // 2. ProjectDefine 에셋 슬롯
        EditorGUILayout.BeginHorizontal();
        m_ProjectDefine = (ProjectDefine)EditorGUILayout.ObjectField("Define Container", m_ProjectDefine, typeof(ProjectDefine), false);
        if (m_ProjectDefine == null)
        {
            if (GUILayout.Button("+ 에셋 생성", GUILayout.Width(80)))
            {
                m_ProjectDefine = ProjectDefine.CreateAssetInUserFolder();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(m_ProjectDefine == null ? "  ↳ 미생성/미할당 시: 기존 PlayerSettings 심볼 유지 (건너뛰기)" : "  ↳ 할당됨: 플랫폼/모드별 전처리기 심볼 자동 주입", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    private BuildContext CreatePreviewContext()
    {
        var target = GetBuildTarget(m_Platform);
        var ctx = new BuildContext
        {
            Target = target,
            TargetGroup = BuildHelper.GetTargetGroup(target),
            BuildType = m_BuildType,
            Version = m_Version,
            VersionCode = m_VersionCode,
            IsDevBuild = m_DevBuild,
            IsDeepProfile = m_DeepProfile,
            IsCleanBuild = m_CleanBuild,
            IsAab = m_IsAab,
            ZipPackage = m_ZipPackage,
            OutputPath = m_OutputPath
        };

        if (m_SceneDefine != null) ctx.SetCustomData("SceneDefine", m_SceneDefine);
        if (m_ProjectDefine != null) ctx.SetCustomData("ProjectDefine", m_ProjectDefine);

        return ctx;
    }

    /// <summary>
    /// 파이프라인 스텝 시각화 및 클릭 시 실시간 코드 미리보기 패널
    /// </summary>
    private void DrawPipelineStepsSequence()
    {
        EditorGUILayout.BeginVertical("box");
        
        var previewContext = CreatePreviewContext();

        EditorGUILayout.BeginHorizontal();
        m_ShowPipelineSteps = EditorGUILayout.Foldout(m_ShowPipelineSteps, $"📋 빌드 파이프라인 실행 순서 ({m_Platform} 타겟 조건부 적용)", true, EditorStyles.foldoutHeader);
        
        if (GUILayout.Button(m_ShowOnlyActiveSteps ? "필터: 조건 만족만" : "필터: 전체 보기", EditorStyles.miniButton, GUILayout.Width(110)))
        {
            m_ShowOnlyActiveSteps = !m_ShowOnlyActiveSteps;
        }

        if (GUILayout.Button("새로고침", EditorStyles.miniButton, GUILayout.Width(60)))
        {
            BuildStepRegistry.ReloadSteps();
        }
        EditorGUILayout.EndHorizontal();

        if (m_ShowPipelineSteps)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("💡 스텝 토글(체크박스)을 끄면 빌드에서 제외되며, 스텝 이름을 클릭하면 하단에 실제 C# 구현 코드를 미리보기할 수 있습니다.", MessageType.None);

            var allPre = BuildStepRegistry.GetAllPreBuildSteps();
            var allPost = BuildStepRegistry.GetAllPostBuildSteps();

            // 1. 사전 빌드 스텝들
            EditorGUILayout.LabelField("▶ 1단계: 사전 빌드 설정 (Pre-Build Steps)", EditorStyles.miniBoldLabel);

            int shownPreCount = 0;
            for (int i = 0; i < allPre.Count; i++)
            {
                var step = allPre[i];
                bool conditionActive = step.IsActive(previewContext);

                if (m_ShowOnlyActiveSteps && !conditionActive)
                    continue;

                shownPreCount++;
                DrawStepRow(step.StepId, step.StepName, step.ExecutionOrder, conditionActive, step.GetType());
            }

            if (shownPreCount == 0)
            {
                EditorGUILayout.HelpBox("현재 조건에서 실행될 Pre-Build Step이 없습니다.", MessageType.None);
            }

            EditorGUILayout.Space(2);

            // 2. 실제 유니티 빌드 코어
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.color = new Color(0.4f, 0.9f, 0.5f);
            GUILayout.Label("⚡ [Core]", EditorStyles.boldLabel, GUILayout.Width(65));
            GUILayout.Label($"Unity BuildPipeline.BuildPlayer ({m_Platform} 플레이어 빌드 수행)", EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 3. 사후 빌드 스텝들
            EditorGUILayout.LabelField("▶ 2단계: 사후 빌드 처리 (Post-Build Steps)", EditorStyles.miniBoldLabel);

            int shownPostCount = 0;
            for (int i = 0; i < allPost.Count; i++)
            {
                var step = allPost[i];
                bool conditionActive = step.IsActive(previewContext);

                if (m_ShowOnlyActiveSteps && !conditionActive)
                    continue;

                shownPostCount++;
                DrawStepRow(step.StepId, step.StepName, step.ExecutionOrder, conditionActive, step.GetType());
            }

            if (shownPostCount == 0)
            {
                EditorGUILayout.HelpBox("현재 조건에서 실행될 Post-Build Step이 없습니다.", MessageType.None);
            }

            // 4. 선택된 스텝 코드 미리보기 패널 노출!
            DrawCodePreviewSection();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStepRow(string stepId, string stepName, int executionOrder, bool conditionActive, Type stepType)
    {
        bool isEnabled = BuildStepRegistry.IsStepEnabled(stepId);
        bool isSelected = m_PreviewStepId == stepId;

        // 선택 상태에 따라 배경 하이라이트
        var rowStyle = isSelected ? EditorStyles.selectionRect : EditorStyles.helpBox;
        EditorGUILayout.BeginHorizontal(rowStyle);

        // 1. 활성화/비활성화 체크박스
        bool newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
        if (newEnabled != isEnabled)
        {
            BuildStepRegistry.SetStepEnabled(stepId, newEnabled);
        }

        // 2. 스텝 정보 클릭 버튼 (토글 말고 자체를 누르면 코드 미리보기 토글!)
        string labelText = $"[Order {executionOrder}] {stepName}";
        if (!conditionActive)
            labelText += " (타겟 불일치로 스킵)";
        else if (!newEnabled)
            labelText += " (사용자 비활성화)";

        var buttonStyle = isSelected ? EditorStyles.boldLabel : EditorStyles.label;

        if (isSelected) GUI.color = new Color(0.4f, 0.9f, 1f);
        else if (!conditionActive) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        else if (!newEnabled) GUI.color = new Color(1f, 0.45f, 0.45f, 0.8f);
        else GUI.color = Color.white;

        if (GUILayout.Button(new GUIContent(labelText, "클릭하여 C# 코드 미리보기"), buttonStyle))
        {
            ToggleCodePreview(stepId, stepName, stepType);
        }
        GUI.color = Color.white;

        // 3. 소스 보기 돋보기 아이콘 버튼
        string btnText = isSelected ? "닫기" : "🔍 코드";
        if (GUILayout.Button(new GUIContent(btnText, "C# 구현 코드 미리보기 토글"), EditorStyles.miniButton, GUILayout.Width(55)))
        {
            ToggleCodePreview(stepId, stepName, stepType);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void ToggleCodePreview(string stepId, string stepName, Type stepType)
    {
        if (m_PreviewStepId == stepId)
        {
            // 이미 열려있으면 접기
            m_PreviewStepId = string.Empty;
            m_PreviewClassSource = string.Empty;
            m_PreviewFullSource = string.Empty;
            m_PreviewScriptPath = string.Empty;
            return;
        }

        m_PreviewStepId = stepId;
        m_PreviewStepName = stepName;
        m_PreviewScriptPath = FindScriptFilePath(stepType, out m_PreviewLineNumber, out m_PreviewClassSource, out m_PreviewFullSource);
    }

    private void DrawCodePreviewSection()
    {
        if (string.IsNullOrEmpty(m_PreviewStepId))
            return;

        string displayCode = m_ShowOnlyClassBlock ? m_PreviewClassSource : m_PreviewFullSource;
        if (string.IsNullOrEmpty(displayCode))
            displayCode = "// 소스 코드를 불러올 수 없습니다.";

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical("box");

        // 상단 헤더 툴바
        EditorGUILayout.BeginHorizontal();
        GUI.color = new Color(0.35f, 0.85f, 1f);
        GUILayout.Label($"📄 [C# 소스 미리보기] {m_PreviewStepName}", EditorStyles.boldLabel);
        GUI.color = Color.white;

        // 클래스만 보기 vs 전체 파일 보기 토글 버튼
        if (GUILayout.Button(m_ShowOnlyClassBlock ? "전체 파일 보기" : "이 클래스만 보기", EditorStyles.miniButton, GUILayout.Width(100)))
        {
            m_ShowOnlyClassBlock = !m_ShowOnlyClassBlock;
        }

        if (!string.IsNullOrEmpty(m_PreviewScriptPath))
        {
            if (GUILayout.Button("IDE에서 열기", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                var scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(m_PreviewScriptPath);
                if (scriptAsset != null)
                    AssetDatabase.OpenAsset(scriptAsset, m_PreviewLineNumber);
                else
                    EditorUtility.OpenWithDefaultApp(m_PreviewScriptPath);
            }
            if (GUILayout.Button("에셋 위치 (Ping)", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                var scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(m_PreviewScriptPath);
                if (scriptAsset != null)
                    EditorGUIUtility.PingObject(scriptAsset);
            }
        }

        if (GUILayout.Button("✕ 닫기", EditorStyles.miniButton, GUILayout.Width(55)))
        {
            m_PreviewStepId = string.Empty;
            m_PreviewClassSource = string.Empty;
            m_PreviewFullSource = string.Empty;
        }
        EditorGUILayout.EndHorizontal();

        // 파일 경로 및 라인 정보
        string locInfo = string.IsNullOrEmpty(m_PreviewScriptPath) 
            ? "위치: 경로 미확인" 
            : $"파일: {m_PreviewScriptPath} (Line {m_PreviewLineNumber})";
        EditorGUILayout.LabelField(locInfo, EditorStyles.miniLabel);

        // 코드 내용 텍스트 뷰어 (스크롤뷰)
        m_CodePreviewScroll = EditorGUILayout.BeginScrollView(m_CodePreviewScroll, GUILayout.Height(240));
        EditorGUILayout.TextArea(displayCode, CodePreviewStyle);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private string FindScriptFilePath(Type type, out int lineNumber, out string classCode, out string fullCode)
    {
        lineNumber = 1;
        classCode = string.Empty;
        fullCode = string.Empty;

        if (type == null) return null;

        string targetPath = null;

        // 1. MonoScript 에셋 검색
        var guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Contains($"class {type.Name}"))
                {
                    targetPath = path;
                    fullCode = text;
                    break;
                }
            }
        }

        // 2. DefaultBuildSteps.cs 확인 (여러 스텝 클래스가 모여 있는 기본 파일)
        if (string.IsNullOrEmpty(targetPath))
        {
            string defaultPath = "Assets/Tools/HB_Build/DefaultBuildSteps.cs";
            if (File.Exists(defaultPath))
            {
                string text = File.ReadAllText(defaultPath, Encoding.UTF8);
                if (text.Contains($"class {type.Name}"))
                {
                    targetPath = defaultPath;
                    fullCode = text;
                }
            }
        }

        if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(fullCode))
        {
            classCode = $"// 소스 파일 경로를 찾을 수 없습니다: {type.FullName}";
            return null;
        }

        // 클래스 시작 라인 및 코드 블록 추출
        var lines = fullCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int classStartLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains($"class {type.Name}"))
            {
                classStartLine = i;
                lineNumber = i + 1;
                break;
            }
        }

        if (classStartLine >= 0)
        {
            // 상단 요약 주석(xml doc)이 있으면 주석부터 포함
            int commentStart = classStartLine;
            while (commentStart > 0 && (lines[commentStart - 1].Trim().StartsWith("///") || lines[commentStart - 1].Trim().StartsWith("//")))
            {
                commentStart--;
            }

            // 중괄호 쌍을 맞춰 클래스 종료 지점 탐색
            int braceCount = 0;
            bool foundOpenBrace = false;
            int classEndLine = lines.Length - 1;

            for (int i = classStartLine; i < lines.Length; i++)
            {
                foreach (char ch in lines[i])
                {
                    if (ch == '{')
                    {
                        braceCount++;
                        foundOpenBrace = true;
                    }
                    else if (ch == '}')
                    {
                        braceCount--;
                        if (foundOpenBrace && braceCount <= 0)
                        {
                            classEndLine = i;
                            goto DoneSearch;
                        }
                    }
                }
            }

        DoneSearch:
            var extracted = lines.Skip(commentStart).Take(classEndLine - commentStart + 1);
            classCode = string.Join("\n", extracted);
        }
        else
        {
            classCode = fullCode;
        }

        return targetPath;
    }

    private void DrawVersionSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🏷️ 버전 정보", EditorStyles.boldLabel);

        m_Version = EditorGUILayout.TextField("App Version (BundleVersion)", m_Version);
        m_VersionCode = EditorGUILayout.TextField("Version Code (BuildNumber)", m_VersionCode);

        if (m_Platform == ESelfBuildPlatform.iOS)
        {
            m_ProvisionUUID = EditorGUILayout.TextField("iOS Provision UUID", m_ProvisionUUID);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawOptionsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("⚙️ 빌드 세부 옵션", EditorStyles.boldLabel);

        m_DevBuild = EditorGUILayout.Toggle("Development Build", m_DevBuild);
        if (m_DevBuild)
        {
            EditorGUI.indentLevel++;
            m_DeepProfile = EditorGUILayout.Toggle("Deep Profiling Support", m_DeepProfile);
            EditorGUI.indentLevel--;
        }
        else
        {
            m_DeepProfile = false;
        }

        m_CleanBuild = EditorGUILayout.Toggle("Clean Build Cache", m_CleanBuild);

        if (m_Platform == ESelfBuildPlatform.Android)
        {
            m_IsAab = EditorGUILayout.Toggle("Build Android App Bundle (.aab)", m_IsAab);
        }
        else if (m_Platform == ESelfBuildPlatform.PC)
        {
            m_ZipPackage = EditorGUILayout.Toggle("Zip Package Output (.zip)", m_ZipPackage);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPathSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📁 빌드 출력 경로 (Output Path)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        m_OutputPath = EditorGUILayout.TextField(m_OutputPath);
        if (GUILayout.Button("폴더 선택", GUILayout.Width(75)))
        {
            string selected = EditorUtility.OpenFolderPanel("빌드 출력 폴더 선택", m_OutputPath, "");
            if (!string.IsNullOrEmpty(selected))
                m_OutputPath = selected;
        }
        if (GUILayout.Button("열기", GUILayout.Width(50)))
        {
            if (!Directory.Exists(m_OutputPath))
                Directory.CreateDirectory(m_OutputPath);
            EditorUtility.RevealInFinder(m_OutputPath);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawSceneSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label($"🎬 빌드 씬 목록 ({m_ScenePath.Count}개)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Build Settings에서 불러오기", GUILayout.Height(24)))
        {
            LoadScenesFromBuildSettings();
        }
        if (GUILayout.Button("현재 씬 추가", GUILayout.Height(24)))
        {
            string currentScene = SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(currentScene) && !m_ScenePath.Contains(currentScene))
                m_ScenePath.Add(currentScene);
        }
        if (GUILayout.Button("전체 비우기", GUILayout.Height(24)))
        {
            m_ScenePath.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        int removeIdx = -1;
        for (int i = 0; i < m_ScenePath.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{i + 1}. {m_ScenePath[i]}", EditorStyles.miniLabel);
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                removeIdx = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (removeIdx >= 0)
        {
            m_ScenePath.RemoveAt(removeIdx);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawActionButtons()
    {
        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        if (GUILayout.Button("🚀 Local Player Build 실행", GUILayout.Height(38)))
        {
            ExecuteLocalBuild();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("📄 Generate Jenkinsfile (Groovy Pipeline 생성)", GUILayout.Height(34)))
        {
            GenerateJenkinsfile();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(2);

        if (GUILayout.Button("📋 Jenkins CLI 명령어 복사", GUILayout.Height(28)))
        {
            string cliCmd = GetCommandLinePreview();
            GUIUtility.systemCopyBuffer = cliCmd;
            EditorUtility.DisplayDialog("복사 완료", $"Jenkins 배치 명령어가 클립보드에 복사되었습니다:\n\n{cliCmd}", "확인");
        }
    }

    private void LoadScenesFromBuildSettings()
    {
        m_ScenePath.Clear();
        var scenes = BuildHelper.GetDefaultBuildScenes();
        m_ScenePath.AddRange(scenes);
    }

    private BuildTarget GetBuildTarget(ESelfBuildPlatform platform)
    {
        return platform switch
        {
            ESelfBuildPlatform.PC => BuildTarget.StandaloneWindows64,
            ESelfBuildPlatform.Android => BuildTarget.Android,
            ESelfBuildPlatform.iOS => BuildTarget.iOS,
            _ => BuildTarget.StandaloneWindows64
        };
    }

    private void ExecuteLocalBuild()
    {
        var target = GetBuildTarget(m_Platform);
        var scenes = m_ScenePath.Count > 0 ? m_ScenePath.ToArray() : BuildHelper.GetDefaultBuildScenes();

        string outDir = string.IsNullOrEmpty(m_OutputPath) ? BuildHelper.GetDefaultOutputPath(target) : m_OutputPath;
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        string ext = target switch
        {
            BuildTarget.Android => m_IsAab ? ".aab" : ".apk",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
            _ => ""
        };

        string appName = $"{Application.productName}_v{m_Version}_{m_BuildType}{ext}";
        string locationPath = Path.Combine(outDir, appName);

        var context = new BuildContext
        {
            Target = target,
            TargetGroup = BuildHelper.GetTargetGroup(target),
            BuildType = m_BuildType,
            Version = m_Version,
            VersionCode = m_VersionCode,
            IsDevBuild = m_DevBuild,
            IsDeepProfile = m_DeepProfile,
            IsCleanBuild = m_CleanBuild,
            IsAab = m_IsAab,
            ZipPackage = m_ZipPackage,
            OutputPath = outDir,
            Scenes = scenes.ToList()
        };

        if (m_SceneDefine != null)
            context.SetCustomData("SceneDefine", m_SceneDefine);

        if (m_ProjectDefine != null)
            context.SetCustomData("ProjectDefine", m_ProjectDefine);

        if (m_Platform == ESelfBuildPlatform.iOS)
        {
            context.SetCustomData("ProvisionUUID", m_ProvisionUUID);
        }

        context.PlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            target = target,
            targetGroup = context.TargetGroup,
            locationPathName = locationPath,
            options = BuildOptions.CompressWithLz4HC
        };

        if (m_DevBuild)
        {
            context.PlayerOptions.options |= BuildOptions.Development;
            if (m_DeepProfile) context.PlayerOptions.options |= BuildOptions.EnableDeepProfilingSupport;
        }
        if (m_CleanBuild)
        {
            context.PlayerOptions.options |= BuildOptions.CleanBuildCache;
        }

        AbsBuild.Execute(context);

        EditorUtility.DisplayDialog("빌드 완료", "플레이어 빌드가 성공적으로 완료되었습니다!", "확인");
    }

    private void GenerateJenkinsfile()
    {
        string targetStr = GetBuildTarget(m_Platform).ToString();
        string sceneArg = m_ScenePath.Count > 0 ? string.Join(";", m_ScenePath) : "NONE";

        var sb = new StringBuilder();
        sb.AppendLine("// ==========================================================================");
        sb.AppendLine("// Jenkins Declarative Pipeline - Generated by HB Custom Build Manager");
        sb.AppendLine($"// Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("// ==========================================================================");
        sb.AppendLine("pipeline {");
        sb.AppendLine("    agent any");
        sb.AppendLine();
        sb.AppendLine("    parameters {");
        sb.AppendLine($"        choice(name: 'BUILD_TARGET', choices: ['{targetStr}', 'StandaloneWindows64', 'Android', 'iOS'], description: '빌드 타겟 플랫폼')");
        sb.AppendLine($"        choice(name: 'BUILD_TYPE', choices: ['{m_BuildType}', 'DEV', 'QA', 'LIVE'], description: '빌드 환경 모드')");
        sb.AppendLine($"        string(name: 'BUILD_VERSION', defaultValue: '{m_Version}', description: '앱 버전 (Bundle Version)')");
        sb.AppendLine($"        string(name: 'VERSION_CODE', defaultValue: '{m_VersionCode}', description: '버전 코드 (Android/iOS)')");
        sb.AppendLine($"        booleanParam(name: 'DEV_BUILD', defaultValue: {m_DevBuild.ToString().ToLower()}, description: 'Development Build 활성화')");
        sb.AppendLine($"        booleanParam(name: 'CLEAN_BUILD', defaultValue: {m_CleanBuild.ToString().ToLower()}, description: 'Clean Build Cache')");
        sb.AppendLine($"        booleanParam(name: 'IS_AAB', defaultValue: {m_IsAab.ToString().ToLower()}, description: 'Android App Bundle(.aab) 빌드 여부')");
        sb.AppendLine($"        booleanParam(name: 'ZIP_PACKAGE', defaultValue: {m_ZipPackage.ToString().ToLower()}, description: 'PC 빌드 결과물 ZIP 압축 여부')");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    environment {");
        sb.AppendLine("        UNITY_PATH = 'C:/Program Files/Unity/Hub/Editor/6000.0.46f1/Editor/Unity.exe'");
        sb.AppendLine("        PROJECT_PATH = \"${WORKSPACE}\"");
        sb.AppendLine("        OUTPUT_DIR = \"${WORKSPACE}/BuildOutput\"");
        sb.AppendLine("        LOG_FILE = \"${WORKSPACE}/build.log\"");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    stages {");
        sb.AppendLine("        stage('Checkout SCM') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                checkout scm");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        stage('Unity Player Build') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                script {");
        sb.AppendLine("                    echo \"Starting Unity CLI Build: Target=${params.BUILD_TARGET}, Type=${params.BUILD_TYPE}, Ver=${params.BUILD_VERSION}\"");
        sb.AppendLine("                    def unityArgs = \"\"\"");
        sb.AppendLine("                        \\\"${UNITY_PATH}\\\" -batchmode -quit \\");
        sb.AppendLine("                        -projectPath \\\"${PROJECT_PATH}\\\" \\");
        sb.AppendLine("                        -executeMethod ProjectBuild.JenkinsBuild \\");
        sb.AppendLine("                        -logFile \\\"${LOG_FILE}\\\" \\");
        sb.AppendLine("                        -buildTarget ${params.BUILD_TARGET} \\");
        sb.AppendLine("                        -buildType ${params.BUILD_TYPE} \\");
        sb.AppendLine("                        -buildVersion ${params.BUILD_VERSION} \\");
        sb.AppendLine("                        -versionCode ${params.VERSION_CODE} \\");
        sb.AppendLine("                        -devBuild ${params.DEV_BUILD} \\");
        sb.AppendLine("                        -cleanBuild ${params.CLEAN_BUILD} \\");
        sb.AppendLine("                        -isAab ${params.IS_AAB} \\");
        sb.AppendLine("                        -zipPackage ${params.ZIP_PACKAGE} \\");
        sb.AppendLine($"                        -sceneList \\\"{sceneArg}\\\" \\");
        sb.AppendLine("                        -outputPath \\\"${OUTPUT_DIR}\\\"");
        sb.AppendLine("                    \"\"\".stripIndent().trim()");
        sb.AppendLine();
        sb.AppendLine("                    if (isUnix()) {");
        sb.AppendLine("                        sh unityArgs");
        sb.AppendLine("                    } else {");
        sb.AppendLine("                        bat unityArgs");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        stage('Archive Artifacts') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                archiveArtifacts artifacts: 'BuildOutput/**', allowEmptyArchive: true");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    post {");
        sb.AppendLine("        always {");
        sb.AppendLine("            archiveArtifacts artifacts: 'build.log', allowEmptyArchive: true");
        sb.AppendLine("        }");
        sb.AppendLine("        success {");
        sb.AppendLine("            echo 'Unity Build Completed Successfully!'");
        sb.AppendLine("        }");
        sb.AppendLine("        failure {");
        sb.AppendLine("            echo 'Unity Build Failed. Check build.log for details.'");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        string groovyCode = sb.ToString();
        GUIUtility.systemCopyBuffer = groovyCode;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string jenkinsfilePath = Path.Combine(projectRoot, "Jenkinsfile");

        bool saveFile = EditorUtility.DisplayDialog(
            "Jenkinsfile (Groovy) 생성 완료",
            "Jenkinsfile Groovy 스크립트가 생성되어 클립보드에 복사되었습니다!\n\n프로젝트 루트에 'Jenkinsfile' 파일로 바로 저장하시겠습니까?",
            "Jenkinsfile 파일로 저장",
            "클립보드 복사만 유지"
        );

        if (saveFile)
        {
            File.WriteAllText(jenkinsfilePath, groovyCode, Encoding.UTF8);
            EditorUtility.DisplayDialog("저장 완료", $"파일이 성공적으로 저장되었습니다:\n{jenkinsfilePath}", "확인");
            EditorUtility.RevealInFinder(jenkinsfilePath);
        }
    }

    private string GetCommandLinePreview()
    {
        string targetStr = GetBuildTarget(m_Platform).ToString();
        string sceneArg = m_ScenePath.Count > 0 ? string.Join(";", m_ScenePath) : "NONE";

        return $"Unity.exe -batchmode -quit -projectPath \"{BuildHelper.ProjectPath}\" " +
               $"-executeMethod ProjectBuild.JenkinsBuild -logFile \"build.log\" " +
               $"-buildTarget {targetStr} -buildType {m_BuildType} -buildVersion {m_Version} " +
               $"-versionCode {m_VersionCode} -devBuild {m_DevBuild.ToString().ToLower()} " +
               $"-cleanBuild {m_CleanBuild.ToString().ToLower()} -isAab {m_IsAab.ToString().ToLower()} " +
               $"-sceneList \"{sceneArg}\" -outputPath \"{m_OutputPath}\"";
    }

    #region Jenkins CLI Entrypoint

    public static void JenkinsBuild()
    {
        BuildHelper.Log("=== Starting Jenkins CI/CD Automated Build ===");

        BuildHelper.SetUpAllArgs();
        BuildHelper.ReadDevBuildState();
        BuildHelper.ReadEnvironment();

        var platformTargetStr = BuildHelper.GetArg(BuildHelper.BUILD_TARGET);
        if (string.IsNullOrEmpty(platformTargetStr) ||
            !Enum.TryParse<BuildTarget>(platformTargetStr, true, out var platformTarget))
        {
            throw BuildHelper.ThrowError($"Missing or invalid -buildTarget argument :: '{platformTargetStr}'");
        }

        BuildHelper.Log($"Resolved Build Platform: {platformTarget}");
        BuildProcess.Build(platformTarget);
    }

    #endregion
}
