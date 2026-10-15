using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

/// <summary>
/// Unity 커스텀 빌드 매니저 및 Jenkins CI/CD 연동 에디터 윈도우입니다.
/// BuildProfile(.asset) 기반의 프로필 관리 및 저장, 다중 버전 Jenkins/Unity Hub CLI 명령어 자동 생성,
/// 체크박스 On/Off, 인라인 C# 소스 코드 미리보기, Groovy Declarative Jenkinsfile 생성을 지원합니다.
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[NoAutoStaticsCleanup]
#endif
public class ProjectBuild : EditorWindow
{
    public enum ESelfBuildPlatform
    {
        PC = 0,
        Android = 1,
        iOS = 2,
    }

    public enum ECliRunnerType
    {
        DirectUnityExe = 0,   // Direct Unity.exe 실행
        UnityHubHeadless = 1  // Unity Hub CLI 헤드리스 실행 (--headless run --project-path)
    }

    public enum ECliCommandMode
    {
        ProfileOnly = 0,      // 1) 순수 프로필 기반 (-buildProfile)
        FullUnpack = 1,       // 2) 전체 파라미터 풀기 (-buildTarget, -buildType, ...)
        ProfileOverride = 2   // 3) 프로필 + 파라미터 오버라이드 (-buildProfile + -buildVersion 등)
    }

    [Header("=== 빌드 프로필 (ScriptableObject) ===")]
    [SerializeField] private HBBuildProfile m_SelectedProfile;

    [Header("=== 빌드 타겟 및 환경 ===")]
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
    [SerializeField] private List<string> m_DisabledStepIds = new List<string>();

    // 프로필 목록 캐시 (OnEnable / OnProjectChange / 생성·복제 시 갱신)
    private List<HBBuildProfile> m_ProfileCache = new List<HBBuildProfile>();
    private string[] m_ProfileNames = Array.Empty<string>();

    // 선택적 설정 에셋 (미할당 시 자동 건너뜀)
    [SerializeField] private SceneDefine m_SceneDefine;
    [SerializeField] private ProjectDefine m_ProjectDefine;

    // CLI 생성 옵션 상태
    private ECliRunnerType m_CliRunner = ECliRunnerType.DirectUnityExe;
    private ECliCommandMode m_CliMode = ECliCommandMode.ProfileOnly;
    private bool m_ShowCliSection = true;

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

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void ResetStaticsOnDomainReload()
    {
        s_CodePreviewStyle = null;
    }
#endif

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
        window.minSize = new Vector2(490, 800);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshProfileCache();

        if (m_SelectedProfile == null && m_ProfileCache.Count > 0)
        {
            m_SelectedProfile = m_ProfileCache[0];
            LoadFromProfile(m_SelectedProfile);
        }

        if (string.IsNullOrEmpty(m_Version))
            m_Version = Application.version;

        if (string.IsNullOrEmpty(m_VersionCode))
            m_VersionCode = PlayerSettings.Android.bundleVersionCode.ToString();

        // m_OutputPath는 비워 두면 기본 경로(BuildOutput/<Target>)를 사용한다.
        // 로컬 절대 경로가 프로필에 저장되어 CI에서 쓰이지 않도록 자동으로 채우지 않는다.

        if (m_ScenePath.Count == 0)
            LoadScenesFromBuildSettings();

        if (m_SceneDefine == null)
            m_SceneDefine = SceneDefine.LoadIfExists();

        if (m_ProjectDefine == null)
            m_ProjectDefine = ProjectDefine.LoadIfExists();

        BuildStepRegistry.ReloadSteps();
    }

    private void OnProjectChange()
    {
        RefreshProfileCache();
        Repaint();
    }

    private void RefreshProfileCache()
    {
        m_ProfileCache = HBBuildProfile.FindAllProfiles();
        m_ProfileNames = m_ProfileCache.Select(p => p.name).ToArray();
    }

    private void OnGUI()
    {
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

        DrawHeader();
        EditorGUILayout.Space(8);

        DrawProfileSection();
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
        EditorGUILayout.Space(10);

        DrawCommandLineGeneratorSection();
        EditorGUILayout.Space(14);

        DrawActionButtons();

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("HB Custom Build Manager", EditorStyles.boldLabel);
        GUILayout.Label("모듈형 파이프라인(IPreBuildStep/IPostBuildStep), BuildProfile(.asset) 및 다중 Jenkins/Unity CLI 생성기", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawProfileSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("빌드 프로필 (Build Profile .asset)", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("+ 새 프로필", EditorStyles.miniButton, GUILayout.Width(75)))
        {
            CreateNewProfileDialog();
        }

        if (m_SelectedProfile != null)
        {
            if (GUILayout.Button("프로필 저장", EditorStyles.miniButton, GUILayout.Width(75)))
            {
                SaveToCurrentProfile();
            }
            if (GUILayout.Button("복제", EditorStyles.miniButton, GUILayout.Width(45)))
            {
                DuplicateCurrentProfile();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var newProfile = (HBBuildProfile)EditorGUILayout.ObjectField("선택된 프로필", m_SelectedProfile, typeof(HBBuildProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            m_SelectedProfile = newProfile;
            if (m_SelectedProfile != null)
            {
                LoadFromProfile(m_SelectedProfile);
            }
        }

        // 전체 프로필 팝업 메뉴
        if (m_ProfileCache.Count > 0)
        {
            int currentIndex = m_ProfileCache.IndexOf(m_SelectedProfile);

            int selectedIndex = EditorGUILayout.Popup(currentIndex, m_ProfileNames, GUILayout.Width(130));
            if (selectedIndex >= 0 && selectedIndex < m_ProfileCache.Count && selectedIndex != currentIndex)
            {
                m_SelectedProfile = m_ProfileCache[selectedIndex];
                LoadFromProfile(m_SelectedProfile);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (m_SelectedProfile != null)
        {
            string profilePath = AssetDatabase.GetAssetPath(m_SelectedProfile);
            EditorGUILayout.LabelField($"  ↳ 에셋 경로: {profilePath}", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.HelpBox("선택된 프로필이 없습니다. [+ 새 프로필] 버튼을 눌러 프로젝트별 빌드 프로필(.asset)을 생성하거나 할당하세요.", MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    private void CreateNewProfileDialog()
    {
        string defaultName = $"BuildProfile_{m_Platform}_{m_BuildType}";
        var newProfile = HBBuildProfile.CreateNewProfileAsset(defaultName);
        if (newProfile != null)
        {
            SaveToProfileInstance(newProfile);
            m_SelectedProfile = newProfile;
            EditorUtility.SetDirty(m_SelectedProfile);
            AssetDatabase.SaveAssets();
            RefreshProfileCache();
            EditorGUIUtility.PingObject(m_SelectedProfile);
            ShowNotification(new GUIContent($"새 프로필 생성됨: {newProfile.name}"));
        }
    }

    private void DuplicateCurrentProfile()
    {
        if (m_SelectedProfile == null) return;

        string currentPath = AssetDatabase.GetAssetPath(m_SelectedProfile);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(currentPath);
        if (AssetDatabase.CopyAsset(currentPath, newPath))
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshProfileCache();
            var duplicated = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(newPath);
            if (duplicated != null)
            {
                m_SelectedProfile = duplicated;
                EditorGUIUtility.PingObject(m_SelectedProfile);
                ShowNotification(new GUIContent($"프로필 복제 완료: {duplicated.name}"));
            }
        }
    }

    private void LoadFromProfile(HBBuildProfile profile)
    {
        if (profile == null) return;

        m_Platform = profile.TargetPlatform;
        m_BuildType = profile.BuildType;
        m_Version = profile.Version;
        m_VersionCode = profile.VersionCode;
        m_DevBuild = profile.DevBuild;
        m_DeepProfile = profile.DeepProfile;
        m_CleanBuild = profile.CleanBuild;
        m_IsAab = profile.IsAab;
        m_ZipPackage = profile.ZipPackage;
        m_ProvisionUUID = profile.ProvisionUUID;
        m_OutputPath = profile.OutputPath;

        if (profile.Scenes != null && profile.Scenes.Count > 0)
        {
            m_ScenePath = profile.Scenes.ToList();
        }
        else
        {
            LoadScenesFromBuildSettings();
        }

        m_SceneDefine = profile.SceneDefineAsset;
        m_ProjectDefine = profile.ProjectDefineAsset;
        m_DisabledStepIds = profile.DisabledStepIds != null ? profile.DisabledStepIds.ToList() : new List<string>();

        Repaint();
    }

    private void SaveToCurrentProfile()
    {
        if (m_SelectedProfile == null)
        {
            CreateNewProfileDialog();
            return;
        }

        Undo.RecordObject(m_SelectedProfile, "Save Build Profile Settings");
        SaveToProfileInstance(m_SelectedProfile);
        EditorUtility.SetDirty(m_SelectedProfile);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"프로필 저장 완료: '{m_SelectedProfile.name}'"));
    }

    /// <summary>
    /// 창의 현재 값이 선택된 프로필 에셋과 다른지 확인합니다. (임시 복사본에 창 값을 써서 직렬화 결과를 비교)
    /// </summary>
    private bool HasUnsavedProfileChanges()
    {
        if (m_SelectedProfile == null) return false;

        var temp = Instantiate(m_SelectedProfile);
        try
        {
            temp.name = m_SelectedProfile.name;
            SaveToProfileInstance(temp);
            return EditorJsonUtility.ToJson(temp) != EditorJsonUtility.ToJson(m_SelectedProfile);
        }
        finally
        {
            DestroyImmediate(temp);
        }
    }

    private void SaveToProfileInstance(HBBuildProfile profile)
    {
        profile.TargetPlatform = m_Platform;
        profile.BuildType = m_BuildType;
        profile.Version = m_Version;
        profile.VersionCode = m_VersionCode;
        profile.DevBuild = m_DevBuild;
        profile.DeepProfile = m_DeepProfile;
        profile.CleanBuild = m_CleanBuild;
        profile.IsAab = m_IsAab;
        profile.ZipPackage = m_ZipPackage;
        profile.ProvisionUUID = m_ProvisionUUID;
        // 프로젝트 내부 절대 경로는 상대 경로로 바꿔 저장 (다른 PC/CI에서도 같은 위치를 가리키도록)
        profile.OutputPath = !string.IsNullOrWhiteSpace(m_OutputPath) && Path.IsPathRooted(m_OutputPath)
            ? ToProjectRelativeIfInside(m_OutputPath)
            : m_OutputPath;
        profile.Scenes = m_ScenePath.ToList();
        profile.SceneDefineAsset = m_SceneDefine;
        profile.ProjectDefineAsset = m_ProjectDefine;
        profile.DisabledStepIds = m_DisabledStepIds.ToList();
    }

    private void DrawPlatformSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("타겟 플랫폼 & 환경", EditorStyles.boldLabel);

        m_Platform = (ESelfBuildPlatform)EditorGUILayout.EnumPopup("Target Platform", m_Platform);
        m_BuildType = (EBuildType)EditorGUILayout.EnumPopup("Build Type", m_BuildType);
        EditorGUILayout.EndVertical();
    }

    private void DrawOptionalAssetsSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("선택적 프로젝트 설정 에셋 (Optional Build Configs)", EditorStyles.boldLabel);

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

    /// <summary>
    /// 창에서 현재 편집 중인 값(저장 전 포함)으로 BuildContext를 만듭니다.
    /// 파이프라인 미리보기와 프로필 없는 로컬 빌드가 같은 값을 쓰도록 공용으로 사용합니다.
    /// </summary>
    private BuildContext CreateContextFromWindow()
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
            OutputPath = GetEffectiveOutputPath(),
            Scenes = m_ScenePath.Count > 0 ? m_ScenePath.ToList() : BuildHelper.GetDefaultBuildScenes().ToList()
        };

        ctx.DisabledStepIds.UnionWith(m_DisabledStepIds);

        if (m_SceneDefine != null) ctx.SetCustomData("SceneDefine", m_SceneDefine);
        if (m_ProjectDefine != null) ctx.SetCustomData("ProjectDefine", m_ProjectDefine);
        if (m_Platform == ESelfBuildPlatform.iOS && !string.IsNullOrWhiteSpace(m_ProvisionUUID))
            ctx.SetCustomData("ProvisionUUID", m_ProvisionUUID.Trim());

        ctx.RebuildPlayerOptions();
        return ctx;
    }

    private string GetEffectiveOutputPath()
    {
        return string.IsNullOrWhiteSpace(m_OutputPath)
            ? BuildHelper.GetDefaultOutputPath(GetBuildTarget(m_Platform))
            : m_OutputPath.Trim();
    }

    private void DrawPipelineStepsSequence()
    {
        EditorGUILayout.BeginVertical("box");
        
        // 저장 여부와 관계없이 창에서 선택한 플랫폼/옵션 기준으로 스텝 활성 여부를 표시
        var previewContext = CreateContextFromWindow();

        EditorGUILayout.BeginHorizontal();
        m_ShowPipelineSteps = EditorGUILayout.Foldout(m_ShowPipelineSteps, $"빌드 파이프라인 실행 순서 ({m_Platform} 타겟 조건부 적용)", true, EditorStyles.foldoutHeader);
        
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
            EditorGUILayout.HelpBox("스텝 토글(체크박스)을 끄면 빌드에서 제외되며, 스텝 이름을 클릭하면 하단에 실제 C# 구현 코드를 미리보기할 수 있습니다.", MessageType.None);

            var allPre = BuildStepRegistry.GetAllPreBuildSteps();
            var allPost = BuildStepRegistry.GetAllPostBuildSteps();

            // 1. 사전 빌드 스텝들
            EditorGUILayout.LabelField("▶ 1단계: 사전 빌드 설정 (Pre-Build Steps)", EditorStyles.miniBoldLabel);

            int shownPreCount = 0;
            for (int i = 0; i < allPre.Count; i++ )
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
            Color prevCoreColor = GUI.color;
            GUI.color = new Color(0.4f, 0.9f, 0.5f);
            GUILayout.Label("[Core]", EditorStyles.boldLabel, GUILayout.Width(65));
            GUILayout.Label($"Unity BuildPipeline.BuildPlayer ({m_Platform} 플레이어 빌드 수행)", EditorStyles.boldLabel);
            GUI.color = prevCoreColor;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 3. 사후 빌드 스텝들
            EditorGUILayout.LabelField("▶ 2단계: 사후 빌드 처리 (Post-Build Steps)", EditorStyles.miniBoldLabel);

            int shownPostCount = 0;
            for (int i = 0; i < allPost.Count; i++ )
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

            // 4. 선택된 스텝 코드 미리보기 패널 노출
            DrawCodePreviewSection();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStepRow(string stepId, string stepName, int executionOrder, bool conditionActive, Type stepType)
    {
        bool isEnabled = !m_DisabledStepIds.Contains(stepId);
        bool isSelected = m_PreviewStepId == stepId;

        var rowStyle = isSelected ? EditorStyles.selectionRect : EditorStyles.helpBox;
        EditorGUILayout.BeginHorizontal(rowStyle);

        // 1. 활성화/비활성화 체크박스
        // 체크 상태는 창 값으로 편집되고 [프로필 저장] 시 프로필 에셋에 기록된다
        bool newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
        if (newEnabled != isEnabled)
        {
            if (newEnabled) m_DisabledStepIds.Remove(stepId);
            else m_DisabledStepIds.Add(stepId);
        }

        // 2. 스텝 정보 클릭 버튼
        string labelText = $"[Order {executionOrder}] {stepName}";
        if (!conditionActive)
            labelText += " (타겟 불일치로 스킵)";
        else if (!newEnabled)
            labelText += " (사용자 비활성화)";

        var buttonStyle = isSelected ? EditorStyles.boldLabel : EditorStyles.label;

        Color prevColor = GUI.color;
        if (isSelected) GUI.color = new Color(0.4f, 0.9f, 1f);
        else if (!conditionActive) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        else if (!newEnabled) GUI.color = new Color(1f, 0.45f, 0.45f, 0.8f);
        bool clicked = GUILayout.Button(new GUIContent(labelText, "클릭하여 C# 코드 미리보기"), buttonStyle);
        GUI.color = prevColor;

        if (clicked)
        {
            ToggleCodePreview(stepId, stepName, stepType);
        }

        // 3. 소스 보기 버튼
        string btnText = isSelected ? "닫기" : "코드";
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
        Color prevHeaderColor = GUI.color;
        GUI.color = new Color(0.35f, 0.85f, 1f);
        GUILayout.Label($"[C# 소스 미리보기] {m_PreviewStepName}", EditorStyles.boldLabel);
        GUI.color = prevHeaderColor;

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

        string locInfo = string.IsNullOrEmpty(m_PreviewScriptPath) 
            ? "위치: 경로 미확인" 
            : $"파일: {m_PreviewScriptPath} (Line {m_PreviewLineNumber})";
        EditorGUILayout.LabelField(locInfo, EditorStyles.miniLabel);

        m_CodePreviewScroll = EditorGUILayout.BeginScrollView(m_CodePreviewScroll, GUILayout.Height(240));
        EditorGUILayout.TextArea(displayCode, CodePreviewStyle);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private static string ReadScriptText(string assetPath)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        return script != null ? script.text : null;
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
            // git URL로 설치된 패키지는 Packages/ 경로가 실제 파일 경로가 아니므로 MonoScript.text로 읽는다
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string text = ReadScriptText(path);
            if (text != null && text.Contains($"class {type.Name}"))
            {
                targetPath = path;
                fullCode = text;
                break;
            }
        }

        // 2. DefaultBuildSteps.cs 확인 (Assets 및 Packages 모두 포괄)
        if (string.IsNullOrEmpty(targetPath))
        {
            string[] defaultGuids = AssetDatabase.FindAssets("DefaultBuildSteps t:MonoScript");
            if (defaultGuids != null && defaultGuids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(defaultGuids[0]);
                string text = ReadScriptText(path);
                if (text != null && text.Contains($"class {type.Name}"))
                {
                    targetPath = path;
                    fullCode = text;
                }
            }
        }

        if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(fullCode))
        {
            classCode = $"// 소스 파일 경로를 찾을 수 없습니다: {type.FullName}";
            return null;
        }

        var lines = fullCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int classStartLine = -1;
        for (int i = 0; i < lines.Length; i++ )
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
            int commentStart = classStartLine;
            while (commentStart > 0 && (lines[commentStart - 1].Trim().StartsWith("///") || lines[commentStart - 1].Trim().StartsWith("//")))
            {
                commentStart--;
            }

            int braceCount = 0;
            bool foundOpenBrace = false;
            int classEndLine = lines.Length - 1;

            for (int i = classStartLine; i < lines.Length; i++ )
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
        GUILayout.Label("버전 정보", EditorStyles.boldLabel);

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
        GUILayout.Label("빌드 세부 옵션", EditorStyles.boldLabel);

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
        GUILayout.Label("빌드 출력 경로 (Output Path)", EditorStyles.boldLabel);

        string resolvedOutput = BuildHelper.ResolveProjectRelativePath(GetEffectiveOutputPath());

        EditorGUILayout.BeginHorizontal();
        m_OutputPath = EditorGUILayout.TextField(m_OutputPath);
        if (GUILayout.Button("폴더 선택", GUILayout.Width(75)))
        {
            string selected = EditorUtility.OpenFolderPanel("빌드 출력 폴더 선택", resolvedOutput, "");
            if (!string.IsNullOrEmpty(selected))
                m_OutputPath = ToProjectRelativeIfInside(selected);
        }
        if (GUILayout.Button("열기", GUILayout.Width(50)))
        {
            Directory.CreateDirectory(resolvedOutput);
            EditorUtility.RevealInFinder(resolvedOutput);
        }
        EditorGUILayout.EndHorizontal();

        if (string.IsNullOrWhiteSpace(m_OutputPath))
            EditorGUILayout.LabelField($"  ↳ 비어 있음: 기본 경로 사용 ({GetEffectiveOutputPath()})", EditorStyles.miniLabel);
        else if (Path.IsPathRooted(m_OutputPath))
            EditorGUILayout.HelpBox("절대 경로는 다른 PC나 Jenkins에서 그대로 쓰입니다. 공유할 프로필에는 프로젝트 기준 상대 경로를 권장합니다.", MessageType.Warning);

        EditorGUILayout.EndVertical();
    }

    private static string ToProjectRelativeIfInside(string absolutePath)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
        string full = Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');

        if (full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return full.Substring(root.Length + 1);
        return full;
    }

    private void DrawSceneSection()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label($"빌드 씬 목록 ({m_ScenePath.Count}개)", EditorStyles.boldLabel);

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
        for (int i = 0; i < m_ScenePath.Count; i++ )
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

    #region CLI & Jenkins Command Generator Section

    private void DrawCommandLineGeneratorSection()
    {
        EditorGUILayout.BeginVertical("box");

        m_ShowCliSection = EditorGUILayout.Foldout(m_ShowCliSection, "Jenkins & Unity CLI 명령어 생성기 (다중 버전 지원)", true, EditorStyles.foldoutHeader);
        if (m_ShowCliSection)
        {
            EditorGUILayout.Space(4);

            // 1. 실행 주체 (Runner Type)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("실행 방식 (Runner)", GUILayout.Width(130));
            m_CliRunner = (ECliRunnerType)EditorGUILayout.EnumPopup(m_CliRunner);
            EditorGUILayout.EndHorizontal();

            // 2. 명령어 모드 (Command Mode)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("파라미터 모드 (Mode)", GUILayout.Width(130));
            m_CliMode = (ECliCommandMode)EditorGUILayout.EnumPopup(m_CliMode);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 가이드 설명 레이블
            string modeDesc = m_CliMode switch
            {
                ECliCommandMode.ProfileOnly => "▶ 1) 순수 프로필: '-buildProfile <경로>' 단일 인자로 가장 깔끔하게 프로필 전체 설정을 적용합니다.",
                ECliCommandMode.FullUnpack => "▶ 2) 전체 풀기: 프로필 없이 '-buildTarget', '-buildType', '-buildVersion' 등 모든 개별 CLI 인자를 명시합니다.",
                ECliCommandMode.ProfileOverride => "▶ 3) 프로필+오버라이드: 프로필 기본값 기반으로, CI 환경변수(Version 등)로 필요한 값만 덮어씁니다.",
                _ => ""
            };
            EditorGUILayout.LabelField(modeDesc, EditorStyles.miniLabel);

            string runnerDesc = m_CliRunner switch
            {
                ECliRunnerType.DirectUnityExe => "▶ Runner: 'Unity.exe' 직접 호출 (Jenkins 환경변수 UNITY_PATH 또는 로컬 Unity.exe)",
                ECliRunnerType.UnityHubHeadless => "▶ Runner: 'Unity Hub.exe -- --headless run --project-path' (프로젝트 버전 일치 에디터 자동 탐색)",
                _ => ""
            };
            EditorGUILayout.LabelField(runnerDesc, EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // 실시간 미리보기
            string previewCmd = BuildCommandLine(m_CliRunner, m_CliMode, false);
            EditorGUILayout.TextArea(previewCmd, GUILayout.Height(64));

            EditorGUILayout.Space(4);

            // 액션 버튼 모음
            EditorGUILayout.BeginHorizontal();

            Color prevBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.85f, 1f);
            bool copy = GUILayout.Button("선택한 CLI 명령어 복사", GUILayout.Height(28));
            GUI.backgroundColor = prevBackground;
            if (copy)
            {
                GUIUtility.systemCopyBuffer = previewCmd;
                EditorUtility.DisplayDialog("복사 완료", $"선택된 CLI 명령어가 클립보드에 복사되었습니다!\n\n{previewCmd}", "확인");
            }

            if (GUILayout.Button("배치 파일(.bat) 저장", GUILayout.Height(28), GUILayout.Width(130)))
            {
                ExportBatchScript(previewCmd);
            }

            if (GUILayout.Button("파워셸(.ps1) 저장", GUILayout.Height(28), GUILayout.Width(130)))
            {
                ExportPowerShellScript();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private const string DEFAULT_HUB_PATH = @"C:\Program Files\Unity Hub\Unity Hub.exe";

    private string BuildCommandLine(ECliRunnerType runner, ECliCommandMode mode, bool isJenkinsVar)
    {
        BuildCommandParts(runner, mode, isJenkinsVar, out string exe, out string args);
        return $"\"{exe}\" {args}";
    }

    /// <summary>
    /// 실행 파일과 인자를 분리해 생성합니다. (.ps1은 Start-Process에 둘을 따로 넘겨야 한다)
    /// Jenkins용(isJenkinsVar)은 Jenkinsfile의 parameters/environment 값을 참조합니다.
    /// </summary>
    private void BuildCommandParts(ECliRunnerType runner, ECliCommandMode mode, bool isJenkinsVar, out string exe, out string args)
    {
        string projectPath = isJenkinsVar ? "${PROJECT_PATH}" : BuildHelper.ProjectPath;
        if (string.IsNullOrEmpty(projectPath))
            projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        string logPath = isJenkinsVar ? "${LOG_FILE}" : "build.log";
        string profilePath = isJenkinsVar
            ? "${params.BUILD_PROFILE}"
            : (m_SelectedProfile != null ? AssetDatabase.GetAssetPath(m_SelectedProfile) : "Assets/Editor/HBBuild/Profiles/BuildProfile_PC_DEV.asset");

        var sb = new StringBuilder();

        // Runner
        if (runner == ECliRunnerType.DirectUnityExe)
        {
            exe = isJenkinsVar ? "${UNITY_PATH}" : EditorApplication.applicationPath;
            sb.Append($"-batchmode -quit -projectPath \"{projectPath}\" -executeMethod ProjectBuild.JenkinsBuild -logFile \"{logPath}\"");
        }
        else
        {
            // Jenkins에서는 environment의 UNITY_HUB_PATH를 쓴다 (Groovy 문자열 안에 역슬래시 경로를 넣지 않기 위함)
            exe = isJenkinsVar ? "${UNITY_HUB_PATH}" : DEFAULT_HUB_PATH;
            sb.Append($"-- --headless run --project-path \"{projectPath}\" -- -batchmode -quit -executeMethod ProjectBuild.JenkinsBuild -logFile \"{logPath}\"");
        }

        // Mode arguments
        switch (mode)
        {
            case ECliCommandMode.ProfileOnly:
                sb.Append($" -buildProfile \"{profilePath}\"");
                break;

            case ECliCommandMode.FullUnpack:
                string sceneArg = m_ScenePath.Count > 0 ? string.Join(";", m_ScenePath) : BuildHelper.NONE_SCENE_LIST;

                sb.Append($" -buildTarget {(isJenkinsVar ? "${params.BUILD_TARGET}" : GetBuildTarget(m_Platform).ToString())}");
                sb.Append($" -buildType {(isJenkinsVar ? "${params.BUILD_TYPE}" : m_BuildType.ToString())}");
                sb.Append($" -buildVersion \"{(isJenkinsVar ? "${params.BUILD_VERSION}" : m_Version)}\"");
                sb.Append($" -versionCode \"{(isJenkinsVar ? "${params.VERSION_CODE}" : m_VersionCode)}\"");
                sb.Append($" -devBuild {m_DevBuild.ToString().ToLower()}");
                sb.Append($" -cleanBuild {m_CleanBuild.ToString().ToLower()}");
                sb.Append($" -isAab {m_IsAab.ToString().ToLower()}");
                sb.Append($" -zipPackage {m_ZipPackage.ToString().ToLower()}");
                sb.Append($" -sceneList \"{sceneArg}\"");

                // Jenkins는 기본 경로(workspace/BuildOutput/<Target>)를 써야 archiveArtifacts와 일치한다
                if (!isJenkinsVar)
                    sb.Append($" -outputPath \"{GetEffectiveOutputPath()}\"");

                if (m_DisabledStepIds.Count > 0)
                    sb.Append($" -{BuildHelper.DISABLED_STEPS} \"{string.Join(";", m_DisabledStepIds)}\"");
                break;

            case ECliCommandMode.ProfileOverride:
                sb.Append($" -buildProfile \"{profilePath}\"");
                sb.Append($" -buildVersion \"{(isJenkinsVar ? "${params.BUILD_VERSION}" : m_Version)}\"");
                sb.Append($" -versionCode \"{(isJenkinsVar ? "${params.VERSION_CODE}" : m_VersionCode)}\"");
                sb.Append($" -buildType {m_BuildType}");
                sb.Append($" -devBuild {m_DevBuild.ToString().ToLower()}");
                break;
        }

        args = sb.ToString();
    }

    private void ExportBatchScript(string cmd)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string selected = EditorUtility.SaveFilePanel("배치 스크립트(.bat) 저장", projectRoot, "RunBuild", "bat");
        if (!string.IsNullOrEmpty(selected))
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("echo ===================================================");
            sb.AppendLine("echo   Starting Automated Unity CLI Build Process");
            sb.AppendLine("echo ===================================================");
            sb.AppendLine(cmd);
            sb.AppendLine("echo.");
            sb.AppendLine("if %ERRORLEVEL% EQU 0 (");
            sb.AppendLine("    echo [SUCCESS] Unity Build finished with success code 0.");
            sb.AppendLine(") else (");
            sb.AppendLine("    echo [ERROR] Unity Build failed! Check build.log for details.");
            sb.AppendLine(")");
            sb.AppendLine("pause");

            File.WriteAllText(selected, sb.ToString(), Encoding.Default);
            EditorUtility.DisplayDialog("저장 완료", $"배치 파일이 성공적으로 생성되었습니다:\n{selected}", "확인");
            EditorUtility.RevealInFinder(selected);
        }
    }

    private void ExportPowerShellScript()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string selected = EditorUtility.SaveFilePanel("파워셸 스크립트(.ps1) 저장", projectRoot, "RunBuild", "ps1");
        if (!string.IsNullOrEmpty(selected))
        {
            BuildCommandParts(m_CliRunner, m_CliMode, false, out string exe, out string args);

            var sb = new StringBuilder();
            sb.AppendLine("Write-Host '===================================================' -ForegroundColor Cyan");
            sb.AppendLine("Write-Host '   Starting Automated Unity CLI Build Process' -ForegroundColor Cyan");
            sb.AppendLine("Write-Host '===================================================' -ForegroundColor Cyan");
            sb.AppendLine($"$exe = '{exe.Replace("'", "''")}'");
            sb.AppendLine($"$argLine = '{args.Replace("'", "''")}'");
            // Unity.exe는 GUI 프로그램이라 직접 호출하면 종료를 기다리지 않으므로 -Wait로 대기 후 종료 코드를 읽는다
            sb.AppendLine("$proc = Start-Process -FilePath $exe -ArgumentList $argLine -Wait -PassThru -NoNewWindow");
            sb.AppendLine("$code = $proc.ExitCode");
            sb.AppendLine("if ($code -eq 0) {");
            sb.AppendLine("    Write-Host '[SUCCESS] Unity Build finished successfully!' -ForegroundColor Green");
            sb.AppendLine("} else {");
            sb.AppendLine("    Write-Host \"[ERROR] Unity Build failed with exit code $code!\" -ForegroundColor Red");
            sb.AppendLine("}");
            sb.AppendLine("exit $code");

            File.WriteAllText(selected, sb.ToString(), new UTF8Encoding(true));
            EditorUtility.DisplayDialog("저장 완료", $"파워셸 스크립트가 성공적으로 생성되었습니다:\n{selected}", "확인");
            EditorUtility.RevealInFinder(selected);
        }
    }

    #endregion

    private void DrawActionButtons()
    {
        Color prevBackground = GUI.backgroundColor;

        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        bool build = GUILayout.Button("Local Player Build 실행", GUILayout.Height(38));
        GUI.backgroundColor = prevBackground;

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        bool generate = GUILayout.Button("Generate Jenkinsfile (Groovy Pipeline 생성)", GUILayout.Height(34));
        GUI.backgroundColor = prevBackground;

        if (build)
        {
            // OnGUI 도중 빌드하면 예외 시 GUILayout 그룹이 닫히지 않으므로 GUI 이벤트 밖에서 실행한다
            EditorApplication.delayCall += ExecuteLocalBuild;
        }

        if (generate)
        {
            GenerateJenkinsfile();
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
        // delayCall 시점에 창이 닫혔을 수 있다
        if (this == null) return;

        string buildName = m_SelectedProfile != null ? $"'{m_SelectedProfile.name}' 프로필 기반 " : string.Empty;

        // 프로필 빌드는 저장된 에셋 값으로만 한다. 창에 미저장 변경이 있으면 자동 저장하지 않고 사용자에게 묻는다
        if (m_SelectedProfile != null && HasUnsavedProfileChanges())
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "저장되지 않은 변경",
                $"'{m_SelectedProfile.name}' 프로필에 저장되지 않은 변경이 있습니다.\n\n" +
                "프로필 빌드는 저장된 프로필 값으로 실행됩니다. 창의 변경을 반영하려면 먼저 저장하세요.",
                "저장 후 빌드", "취소", "저장된 값으로 빌드");

            if (choice == 1) return;
            if (choice == 0) SaveToCurrentProfile();
        }

        try
        {
            if (m_SelectedProfile != null)
            {
                AbsBuild.ExecuteFromProfile(m_SelectedProfile);
            }
            else
            {
                AbsBuild.Execute(CreateContextFromWindow());
            }

            EditorUtility.DisplayDialog("빌드 완료", $"{buildName}플레이어 빌드가 성공적으로 완료되었습니다!", "확인");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("빌드 실패", $"{buildName}플레이어 빌드에 실패했습니다.\n\n{ex.Message}\n\n자세한 내용은 Console을 확인하세요.", "확인");
        }
    }

    private void GenerateJenkinsfile()
    {
        var allProfiles = HBBuildProfile.FindAllProfiles();
        var profileChoices = new List<string>();

        if (m_SelectedProfile != null)
        {
            profileChoices.Add(AssetDatabase.GetAssetPath(m_SelectedProfile));
        }

        foreach (var p in allProfiles)
        {
            string pPath = AssetDatabase.GetAssetPath(p);
            if (!profileChoices.Contains(pPath))
                profileChoices.Add(pPath);
        }

        if (profileChoices.Count == 0)
        {
            profileChoices.Add("Assets/Editor/HBBuild/Profiles/BuildProfile_PC_DEV.asset");
        }

        string choicesArrayStr = string.Join(", ", profileChoices.Select(c => $"'{c}'"));
        string defaultProfile = profileChoices[0];

        var sb = new StringBuilder();
        sb.AppendLine("// ==========================================================================");
        sb.AppendLine($"// Jenkins Declarative Pipeline - Generated by HB Custom Build Manager");
        sb.AppendLine($"// Mode: {m_CliMode} | Runner: {m_CliRunner}");
        sb.AppendLine($"// Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("// ==========================================================================");
        sb.AppendLine("pipeline {");
        sb.AppendLine("    agent any");
        sb.AppendLine();
        sb.AppendLine("    parameters {");

        if (m_CliMode == ECliCommandMode.ProfileOnly || m_CliMode == ECliCommandMode.ProfileOverride)
        {
            sb.AppendLine($"        choice(name: 'BUILD_PROFILE', choices: [{choicesArrayStr}], description: '실행할 HBBuildProfile(.asset) 경로')");
        }

        if (m_CliMode == ECliCommandMode.FullUnpack)
        {
            // 현재 선택값을 첫 번째(기본값)로 두고 중복 없이 나열
            string targetStr = GetBuildTarget(m_Platform).ToString();
            var targetChoices = new[] { targetStr, "StandaloneWindows64", "Android", "iOS" }.Distinct();
            var typeChoices = new[] { m_BuildType.ToString() }.Concat(Enum.GetNames(typeof(EBuildType))).Distinct();
            sb.AppendLine($"        choice(name: 'BUILD_TARGET', choices: [{string.Join(", ", targetChoices.Select(c => $"'{c}'"))}], description: '빌드 타겟 플랫폼')");
            sb.AppendLine($"        choice(name: 'BUILD_TYPE', choices: [{string.Join(", ", typeChoices.Select(c => $"'{c}'"))}], description: '빌드 환경 모드')");
        }

        if (m_CliMode == ECliCommandMode.FullUnpack || m_CliMode == ECliCommandMode.ProfileOverride)
        {
            sb.AppendLine($"        string(name: 'BUILD_VERSION', defaultValue: '{m_Version}', description: '앱 버전 (Bundle Version)')");
            sb.AppendLine($"        string(name: 'VERSION_CODE', defaultValue: '{m_VersionCode}', description: '버전 코드 (Android/iOS)')");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    environment {");

        if (m_CliRunner == ECliRunnerType.DirectUnityExe)
        {
            string unityPath = EditorApplication.applicationPath.Replace('\\', '/');
            sb.AppendLine($"        UNITY_PATH = '{unityPath}'");
        }
        else
        {
            sb.AppendLine("        UNITY_HUB_PATH = 'C:/Program Files/Unity Hub/Unity Hub.exe'");
        }

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
        // 이전 빌드 산출물만 지운다 (Library 캐시는 유지, 소스 정리는 Jenkins job의 checkout 설정에 맡김)
        sb.AppendLine("        stage('Clean Build Output') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                dir('BuildOutput') {");
        sb.AppendLine("                    deleteDir()");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        stage('Unity Build Execution') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                script {");
        sb.AppendLine($"                    echo \"Executing Unity Build: Runner={m_CliRunner}, Mode={m_CliMode}\"");

        string rawCmd = BuildCommandLine(m_CliRunner, m_CliMode, true);
        string formattedCmd = rawCmd.Replace(" -", " \\\n                        -");

        sb.AppendLine("                    def unityArgs = \"\"\"");
        sb.AppendLine($"                        {formattedCmd}");
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
            $"현재 선택된 옵션(Runner: {m_CliRunner}, Mode: {m_CliMode}) 기반의 Jenkinsfile Groovy 코드가 클립보드에 복사되었습니다!\n\n프로젝트 루트에 'Jenkinsfile' 파일로 저장하시겠습니까?",
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

    #region Jenkins CLI Entrypoint

    public static void JenkinsBuild()
    {
        BuildHelper.Log("=== Starting Jenkins CI/CD Automated Build ===");

        BuildHelper.SetUpAllArgs();
        BuildHelper.ReadDevBuildState();
        BuildHelper.ReadEnvironment();

        var profileArg = BuildHelper.GetArg(BuildHelper.BUILD_PROFILE);
        if (!string.IsNullOrEmpty(profileArg))
        {
            BuildHelper.Log($"Detected -buildProfile argument: {profileArg}");
            AbsBuild.ExecuteFromProfilePath(profileArg);
            return;
        }

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
