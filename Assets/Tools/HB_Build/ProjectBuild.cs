using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// Unity 커스텀 빌드 매니저 및 Jenkins CI/CD 연동 에디터 윈도우입니다.
    /// BuildProfile(.asset) 기반의 프로필 관리 및 저장, Jenkins/Unity CLI 명령어 자동 생성,
    /// 체크박스 On/Off, 인라인 C# 소스 코드 미리보기, Groovy Declarative Jenkinsfile 생성을 지원합니다.
    /// </summary>
    #if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
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

        public enum EJenkinsCheckout
        {
            Scm = 0,          // job의 SCM 설정으로 워크스페이스에 받음 (checkout scm)
            LocalFolder = 1   // 빌드 PC의 고정 작업 폴더를 git으로 원격 커밋에 맞춤 (fetch + checkout -f)
        }

        // 창은 선택한 프로필 에셋을 직접 편집한다 (창에 보이는 값 = 빌드되는 값). 값 사본을 두지 않는다
        [SerializeField] private HBBuildProfile m_SelectedProfile;
        private SerializedObject m_ProfileSO;

        // 프로필 목록 캐시 (OnEnable / OnProjectChange / 생성·복제 시 갱신)
        private List<HBBuildProfile> m_ProfileCache = new List<HBBuildProfile>();
        private string[] m_ProfileNames = Array.Empty<string>();

        // CLI 생성 옵션 상태
        private EJenkinsCheckout m_JenkinsCheckout = EJenkinsCheckout.Scm;
        private string m_JenkinsProjectDir = string.Empty;   // 빌드 PC의 경로 (Jenkinsfile에 상수로 들어감)
        private string m_JenkinsBranch = string.Empty;       // Jenkins BRANCH 기본값 (비우면 현재 브랜치)
        private string m_JenkinsAgentLabel = string.Empty;   // 비우면 agent any

        // 로컬 빌드 전 git 작업 폴더 정리. 프로필에 저장하지 않고(다른 사람 · PC로 옮겨 가지 않게) 정리를 한 번 하면 둘 다 끈다
        private bool m_LocalGitClean;
        private bool m_LocalGitCleanConfirmed;
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
                SelectProfile(m_ProfileCache[0]);

            BuildStepRegistry.ReloadSteps();
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnProjectChange()
        {
            RefreshProfileCache();
            Repaint();
        }

        private void SelectProfile(HBBuildProfile profile)
        {
            m_SelectedProfile = profile;
            m_ProfileSO = null;
            Repaint();
        }

        /// <summary> 선택한 프로필의 SerializedObject (프로필이 바뀌거나 정리 · 다시 임포트로 객체가 바뀌면 새로 만든다) </summary>
        private SerializedObject ProfileSO
        {
            get
            {
                if (m_ProfileSO == null || m_ProfileSO.targetObject != m_SelectedProfile)
                    m_ProfileSO = new SerializedObject(m_SelectedProfile);
                return m_ProfileSO;
            }
        }

        private SerializedProperty Prop(string name) => ProfileSO.FindProperty(name);

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

            // 프로필 없이는 빌드 · 명령 생성을 하지 않는다 (모든 빌드는 프로필 에셋 하나로)
            if (m_SelectedProfile == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            // 다른 곳(Inspector · Undo · 정리 뒤 다시 임포트)에서 바뀐 값을 읽는다. 편집은 각 영역 끝에서 바로 반영한다
            ProfileSO.Update();

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

            DrawOutputSection();
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
                using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(m_SelectedProfile)))
                {
                    if (GUILayout.Button(new GUIContent("프로필 저장", "창의 편집은 프로필 에셋에 바로 들어갑니다. 디스크에 쓰려면 저장하세요 (빌드할 때도 저장함)"), EditorStyles.miniButton, GUILayout.Width(75)))
                        SaveProfile();
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
                SelectProfile(newProfile);

            // 전체 프로필 팝업 메뉴
            if (m_ProfileCache.Count > 0)
            {
                int currentIndex = m_ProfileCache.IndexOf(m_SelectedProfile);

                int selectedIndex = EditorGUILayout.Popup(currentIndex, m_ProfileNames, GUILayout.Width(130));
                if (selectedIndex >= 0 && selectedIndex < m_ProfileCache.Count && selectedIndex != currentIndex)
                    SelectProfile(m_ProfileCache[selectedIndex]);
            }
            EditorGUILayout.EndHorizontal();

            if (m_SelectedProfile != null)
            {
                string profilePath = AssetDatabase.GetAssetPath(m_SelectedProfile);
                string dirty = EditorUtility.IsDirty(m_SelectedProfile) ? " (저장 안 됨)" : string.Empty;
                EditorGUILayout.LabelField($"  ↳ 에셋 경로: {profilePath}{dirty}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("모든 빌드는 빌드 프로필(.asset) 하나로 합니다. [+ 새 프로필]로 만들거나 기존 프로필을 고르세요.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateNewProfileDialog()
        {
            // 지금 활성 플랫폼 · DEV로 만든다 (나머지는 만든 뒤 창에서 고친다)
            var platform = EditorUserBuildSettings.activeBuildTarget switch
            {
                BuildTarget.Android => ESelfBuildPlatform.Android,
                BuildTarget.iOS => ESelfBuildPlatform.iOS,
                _ => ESelfBuildPlatform.PC
            };
            var newProfile = HBBuildProfile.CreateNewProfileAsset(platform, EBuildType.DEV);
            RefreshProfileCache();
            SelectProfile(newProfile);
            EditorGUIUtility.PingObject(newProfile);
            ShowNotification(new GUIContent($"새 프로필 생성됨: {newProfile.name}"));
        }

        private void DuplicateCurrentProfile()
        {
            if (m_SelectedProfile == null) return;

            // CopyAsset은 디스크 파일을 복사하므로 창에서 고치고 저장 안 한 값이 빠진다 → 메모리의 프로필(창에 보이는 값)을 복제해 만든다
            string newPath = AssetDatabase.GenerateUniqueAssetPath(AssetDatabase.GetAssetPath(m_SelectedProfile));
            var duplicated = Instantiate(m_SelectedProfile);
            duplicated.name = Path.GetFileNameWithoutExtension(newPath);
            AssetDatabase.CreateAsset(duplicated, newPath);
            // 복사본만 저장한다 (SaveAssets는 원본 프로필의 저장 안 한 창 편집까지 디스크에 쓴다)
            AssetDatabase.SaveAssetIfDirty(duplicated);
            RefreshProfileCache();

            SelectProfile(duplicated);
            EditorGUIUtility.PingObject(duplicated);
            ShowNotification(new GUIContent($"프로필 복제 완료: {duplicated.name}"));
        }

        private void SaveProfile()
        {
            AssetDatabase.SaveAssetIfDirty(m_SelectedProfile);
            ShowNotification(new GUIContent($"프로필 저장 완료: '{m_SelectedProfile.name}'"));
        }

        /// <summary> 영역에서 고친 값을 프로필에 반영한다 (Undo · dirty는 SerializedObject가 처리) </summary>
        private void ApplyProfileEdits()
        {
            ProfileSO.ApplyModifiedProperties();
        }

        private static List<string> ReadStrings(SerializedProperty array)
        {
            var list = new List<string>(array.arraySize);
            for (int i = 0; i < array.arraySize; i++)
                list.Add(array.GetArrayElementAtIndex(i).stringValue);
            return list;
        }

        private static void WriteStrings(SerializedProperty array, IList<string> values)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                array.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private ESelfBuildPlatform Platform => m_SelectedProfile.TargetPlatform;

        private void DrawPlatformSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("타겟 플랫폼 & 환경", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.TargetPlatform)), new GUIContent("Target Platform"));
            EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.BuildType)), new GUIContent("Build Type"));
            EditorGUILayout.EndVertical();
            ApplyProfileEdits();
        }

        private void DrawOptionalAssetsSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("선택적 프로젝트 설정 에셋 (Optional Build Configs)", EditorStyles.boldLabel);

            // 빌드는 이 슬롯만 본다 (비어 있으면 기본 경로 · 프로젝트에서 찾지 않고 건너뜀)
            DrawOptionalAssetSlot<SceneDefine>(nameof(HBBuildProfile.SceneDefineAsset), "Scene Define", SceneDefine.CreateAssetInUserFolder,
                "  ↳ 비어 있음: 씬 목록 그대로 빌드 (건너뛰기)", "  ↳ 할당됨: 빌드 환경(DEV/QA/LIVE)별 IgnoreScene 자동 제외");

            EditorGUILayout.Space(4);

            DrawOptionalAssetSlot<ProjectDefine>(nameof(HBBuildProfile.ProjectDefineAsset), "Define Container", ProjectDefine.CreateAssetInUserFolder,
                "  ↳ 비어 있음: 기존 PlayerSettings 심볼 유지 (LIVE는 빌드 실패)", "  ↳ 할당됨: 플랫폼/모드별 전처리기 심볼 자동 주입");

            EditorGUILayout.Space(4);

            DrawOptionalAssetSlot<HBXcodeSettings>(nameof(HBBuildProfile.XcodeSettingsAsset), "Xcode Settings (iOS)", HBXcodeSettings.CreateAssetInUserFolder,
                "  ↳ 비어 있음: Xcode 설정 적용 건너뜀", "  ↳ 할당됨: iOS 빌드 뒤 Info.plist · Capabilities · Build Settings 등 적용");

            EditorGUILayout.EndVertical();
            ApplyProfileEdits();
        }

        /// <summary> 값은 비었는데 ID가 남아 있으면 삭제된 에셋을 가리키는 참조(Missing)다. 빈 칸은 0(None)이다 </summary>
        private static bool IsMissingReference(SerializedProperty prop)
        {
            // 6000.3에는 objectReferenceEntityIdValue가 없고, Unity 7에서는 instanceID 쪽이 컴파일 에러(CS0619)다
#if UNITY_7000_0_OR_NEWER
            return prop.objectReferenceValue == null && prop.objectReferenceEntityIdValue != EntityId.None;
#else
            return prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0;
#endif
        }

        private void DrawOptionalAssetSlot<T>(string propertyName, string label, Func<T> create, string emptyNote, string assignedNote) where T : ScriptableObject
        {
            var prop = Prop(propertyName);
            // 깨진 참조(Missing)도 objectReferenceValue는 null이다. 빌드는 Missing이면 실패하므로 "비어 있음"과 따로 보인다
            bool missing = IsMissingReference(prop);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
            if (prop.objectReferenceValue == null && !missing && GUILayout.Button("+ 에셋 생성", GUILayout.Width(80)))
                prop.objectReferenceValue = CreateOptionalAsset(create);
            EditorGUILayout.EndHorizontal();
            if (missing)
                EditorGUILayout.HelpBox("깨진 참조(Missing)입니다. 이대로 빌드하면 실패합니다. 에셋을 다시 넣거나 슬롯을 비우세요.", MessageType.Error);
            else
                EditorGUILayout.LabelField(prop.objectReferenceValue == null ? emptyNote : assignedNote, EditorStyles.miniLabel);
        }

        /// <summary>
        /// 선택 설정 에셋을 만들거나(이미 있으면 기존 에셋) 가져옵니다.
        /// 실패하면 예외를 OnGUI 밖으로 던지지 않고 대화상자로 알린 뒤, 모달 이후 남은 레이아웃 그리기는 ExitGUI로 건너뜁니다.
        /// </summary>
        private static T CreateOptionalAsset<T>(Func<T> create) where T : ScriptableObject
        {
            try
            {
                return create();
            }
            catch (Exception ex) when (ex is not ExitGUIException)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("에셋 생성 실패", ex.Message, "확인");
                GUIUtility.ExitGUI();
                return null;
            }
        }

        /// <summary>
        /// 파이프라인 미리보기용 컨텍스트 (프로필 값 그대로). 프로필이 빌드할 수 없는 상태(예약 키 등)면 이유를 돌려주고 플랫폼 · 옵션만 채운다.
        /// </summary>
        private BuildContext CreatePreviewContext(out string error)
        {
            error = null;
            try
            {
                return m_SelectedProfile.ToBuildContext();
            }
            catch (JenkinsBuildException ex)
            {
                error = ex.Message;
                var p = m_SelectedProfile;
                var target = p.GetResolvedBuildTarget();
                return new BuildContext
                {
                    Target = target, TargetGroup = BuildHelper.GetTargetGroup(target), BuildType = p.BuildType,
                    IsDevBuild = p.DevBuild, IsDeepProfile = p.DeepProfile, IsCleanBuild = p.CleanBuild, IsAab = p.IsAab, ZipPackage = p.ZipPackage,
                };
            }
        }

        private void DrawPipelineStepsSequence()
        {
            EditorGUILayout.BeginVertical("box");

            var previewContext = CreatePreviewContext(out string previewError);
            if (previewError != null)
                EditorGUILayout.HelpBox(previewError, MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            m_ShowPipelineSteps = EditorGUILayout.Foldout(m_ShowPipelineSteps, $"빌드 파이프라인 실행 순서 ({Platform} 타겟 조건부 적용)", true, EditorStyles.foldoutHeader);

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
                GUILayout.Label($"Unity BuildPipeline.BuildPlayer ({Platform} 플레이어 빌드 수행)", EditorStyles.boldLabel);
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

                DrawUnknownDisabledSteps();

                // 4. 선택된 스텝 코드 미리보기 패널 노출
                DrawCodePreviewSection();
            }

            EditorGUILayout.EndVertical();
            ApplyProfileEdits();
        }

        private SerializedProperty DisabledStepsProp => Prop(nameof(HBBuildProfile.DisabledStepIds));

        /// <summary>
        /// 비활성 목록에 있지만 등록된 스텝이 없는 ID(스텝 이름 변경 · 삭제)를 보여주고 제거할 수 있게 합니다.
        /// LIVE · 배치 빌드는 이런 ID가 있으면 실패합니다.
        /// </summary>
        private void DrawUnknownDisabledSteps()
        {
            var disabled = ReadStrings(DisabledStepsProp);
            var unknown = BuildStepRegistry.GetUnknownStepIds(disabled);
            if (unknown.Count == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox($"비활성 목록에 등록되지 않은 스텝 ID가 {unknown.Count}개 있습니다 (스텝 이름 변경 · 삭제). LIVE · 배치(Jenkins) 빌드는 실패합니다. 제거 후 [프로필 저장]하세요.", MessageType.Warning);

            string removeId = null;
            foreach (string id in unknown)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(id, EditorStyles.miniLabel);
                if (GUILayout.Button("제거", EditorStyles.miniButton, GUILayout.Width(45)))
                    removeId = id;
                EditorGUILayout.EndHorizontal();
            }

            if (removeId != null)
            {
                disabled.RemoveAll(x => x == removeId);
                WriteStrings(DisabledStepsProp, disabled);
            }
        }

        private void DrawStepRow(string stepId, string stepName, int executionOrder, bool conditionActive, Type stepType)
        {
            var disabled = ReadStrings(DisabledStepsProp);
            bool isEnabled = !disabled.Contains(stepId);
            bool isSelected = m_PreviewStepId == stepId;

            var rowStyle = isSelected ? EditorStyles.selectionRect : EditorStyles.helpBox;
            EditorGUILayout.BeginHorizontal(rowStyle);

            // 1. 활성화/비활성화 체크박스 (프로필의 DisabledStepIds에 바로 들어간다)
            bool newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
            if (newEnabled != isEnabled)
            {
                if (newEnabled) disabled.Remove(stepId);
                else disabled.Add(stepId);
                WriteStrings(DisabledStepsProp, disabled);
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

            EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.Version)), new GUIContent("App Version (BundleVersion)"));
            EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.VersionCode)), new GUIContent("Version Code (BuildNumber)"));

            if (Platform == ESelfBuildPlatform.iOS)
            {
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.ProvisionUUID)), new GUIContent("iOS Provision UUID"));
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.ProvisionType)), new GUIContent("iOS Provision Type", "UUID가 있을 때 수동 서명 종류. AutoSign은 UUID 없이 자동 서명"));
            }

            EditorGUILayout.EndVertical();
            ApplyProfileEdits();
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("빌드 세부 옵션", EditorStyles.boldLabel);

            var devBuild = Prop(nameof(HBBuildProfile.DevBuild));
            EditorGUILayout.PropertyField(devBuild, new GUIContent("Development Build"));
            // Deep Profiling · Profiler 자동 연결은 Development Build일 때만 적용된다 (값은 프로필에 남김)
            if (devBuild.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.DeepProfile)), new GUIContent("Deep Profiling Support"));
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.AutoConnectProfiler)), new GUIContent("Autoconnect Profiler"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.CleanBuild)), new GUIContent("Clean Build Cache"));

            if (Platform == ESelfBuildPlatform.Android)
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.IsAab)), new GUIContent("Build Android App Bundle (.aab)"));
            else if (Platform == ESelfBuildPlatform.PC)
                EditorGUILayout.PropertyField(Prop(nameof(HBBuildProfile.ZipPackage)), new GUIContent("Zip Package Output (.zip)"));

            EditorGUILayout.EndVertical();
            ApplyProfileEdits();
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("빌드 출력 경로 (Output Path)", EditorStyles.boldLabel);

            string outputPath = BuildHelper.GetOutputPath(m_SelectedProfile.GetResolvedBuildTarget());
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(outputPath + "/", EditorStyles.textField);
            if (GUILayout.Button("열기", GUILayout.Width(50)))
            {
                string resolved = BuildHelper.ResolveProjectRelativePath(outputPath);
                Directory.CreateDirectory(resolved);
                EditorUtility.RevealInFinder(resolved);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("  ↳ 로컬 · Jenkins 모두 이 경로에 만듭니다 (바꿀 수 없음)", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawSceneSection()
        {
            var scenesProp = Prop(nameof(HBBuildProfile.Scenes));
            var scenes = ReadStrings(scenesProp);
            bool changed = false;

            EditorGUILayout.BeginVertical("box");
            GUILayout.Label($"빌드 씬 목록 ({scenes.Count}개)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Settings에서 불러오기", GUILayout.Height(24)))
            {
                scenes = BuildHelper.GetDefaultBuildScenes().ToList();
                changed = true;
            }
            if (GUILayout.Button("현재 씬 추가", GUILayout.Height(24)))
            {
                string currentScene = SceneManager.GetActiveScene().path;
                if (!string.IsNullOrEmpty(currentScene) && !scenes.Contains(currentScene))
                {
                    scenes.Add(currentScene);
                    changed = true;
                }
            }
            if (GUILayout.Button("전체 비우기", GUILayout.Height(24)))
            {
                scenes.Clear();
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (scenes.All(string.IsNullOrWhiteSpace))
                EditorGUILayout.LabelField("  ↳ 비어 있음: 빌드할 때 Build Settings의 활성 씬을 씁니다", EditorStyles.miniLabel);

            int removeIdx = -1;
            for (int i = 0; i < scenes.Count; i++ )
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1}. {scenes[i]}", EditorStyles.miniLabel);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    removeIdx = i;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                scenes.RemoveAt(removeIdx);
                changed = true;
            }

            EditorGUILayout.EndVertical();

            if (changed)
                WriteStrings(scenesProp, scenes);
            ApplyProfileEdits();
        }

        #region CLI & Jenkins Command Generator Section

        private void DrawCommandLineGeneratorSection()
        {
            EditorGUILayout.BeginVertical("box");

            m_ShowCliSection = EditorGUILayout.Foldout(m_ShowCliSection, "Jenkins & Unity CLI 명령어 생성기", true, EditorStyles.foldoutHeader);
            if (m_ShowCliSection)
            {
                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("▶ 프로필 빌드: '-buildProfile <경로>'. Jenkins는 버전 · 버전 코드 · Development만 파라미터로 덮어쓸 수 있습니다 (비우면 프로필 값).", EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.LabelField("▶ 실행: 'Unity.exe' 직접 호출 (Jenkins는 에이전트의 UNITY_PATH 또는 ProjectVersion.txt 버전, 로컬은 현재 에디터)", EditorStyles.miniLabel);

                EditorGUILayout.Space(4);

                // Jenkinsfile 소스 갱신 방식 (CLI 명령 미리보기에는 영향 없음)
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Jenkins 체크아웃", GUILayout.Width(130));
                m_JenkinsCheckout = (EJenkinsCheckout)EditorGUILayout.EnumPopup(m_JenkinsCheckout);
                EditorGUILayout.EndHorizontal();

                if (m_JenkinsCheckout == EJenkinsCheckout.Scm)
                {
                    EditorGUILayout.LabelField("▶ job의 SCM 설정으로 워크스페이스에 받습니다 (checkout scm, Pipeline script from SCM).", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("▶ 빌드 PC의 작업 폴더를 원격 커밋에 강제로 맞춥니다 (fetch → checkout -f → clean -fd → lfs pull). 폴더의 수정 · 로컬 커밋은 버려집니다.", EditorStyles.wordWrappedMiniLabel);
                    m_JenkinsProjectDir = EditorGUILayout.TextField(new GUIContent("빌드 PC 프로젝트 폴더", "이 PC가 아니라 빌드 PC의 경로입니다. Jenkinsfile에 상수로 들어갑니다 (빌드 파라미터로 바꿀 수 없음, job마다 폴더 하나)."), m_JenkinsProjectDir);
                    m_JenkinsBranch = EditorGUILayout.TextField(new GUIContent("기본 브랜치", "Jenkins BRANCH 기본값. 비우면 작업 폴더의 현재 브랜치를 씁니다."), m_JenkinsBranch);
                    m_JenkinsAgentLabel = EditorGUILayout.TextField(new GUIContent("에이전트 라벨", "그 폴더가 있는 빌드 PC의 Jenkins 노드 라벨. 비우면 agent any (노드가 하나일 때만)."), m_JenkinsAgentLabel);
                }

                EditorGUILayout.Space(4);

                // 실시간 미리보기
                string previewCmd = BuildCommandLine(false);
                EditorGUILayout.TextArea(previewCmd, GUILayout.Height(64));

                EditorGUILayout.Space(4);

                // 액션 버튼 모음
                EditorGUILayout.BeginHorizontal();

                Color prevBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.85f, 1f);
                bool copy = GUILayout.Button("CLI 명령어 복사", GUILayout.Height(28));
                GUI.backgroundColor = prevBackground;
                if (copy)
                {
                    GUIUtility.systemCopyBuffer = previewCmd;
                    EditorUtility.DisplayDialog("복사 완료", $"CLI 명령어가 클립보드에 복사되었습니다!\n\n{previewCmd}", "확인");
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

        // Jenkins choice 파라미터에서 "프로필 값 사용"을 뜻하는 값
        private const string PROFILE_VALUE = "(profile)";

        /// <summary> Groovy 작은따옴표 문자열 리터럴 (경로에 ' 나 \ 가 있어도 Jenkinsfile 문법이 깨지지 않게) </summary>
        private static string GroovyQuote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        private string BuildCommandLine(bool isJenkinsVar)
        {
            BuildCommandParts(isJenkinsVar, out string exe, out string args);
            return $"\"{exe}\" {args}";
        }

        private void BuildCommandParts(bool isJenkinsVar, out string exe, out string args)
        {
            BuildCommandParts(isJenkinsVar, out exe, out List<string> argList);
            args = string.Join(" ", argList);
        }

        /// <summary>
        /// 실행 파일과 인자를 분리해 생성합니다. (.ps1은 Start-Process에 둘을 따로 넘겨야 한다)
        /// Jenkins용(isJenkinsVar)은 Jenkinsfile의 parameters/environment 값을 참조하고, 덮어쓰기 인자는 BuildJenkinsfileText가 값을 넣은 파라미터만 덧붙인다.
        /// (Unity Hub 헤드리스 CLI에는 프로젝트를 실행하는 명령이 없어 Unity.exe를 직접 호출한다)
        /// </summary>
        private void BuildCommandParts(bool isJenkinsVar, out string exe, out List<string> argList)
        {
            string projectPath = isJenkinsVar ? "${PROJECT_PATH}" : BuildHelper.ProjectPath;
            if (string.IsNullOrEmpty(projectPath))
                projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string logPath = isJenkinsVar ? "${LOG_FILE}" : "build.log";
            string profilePath = isJenkinsVar
                ? "${profilePath}"   // Jenkinsfile 스크립트가 BUILD_PROFILE_PATH(직접 입력) 또는 BUILD_PROFILE로 정함
                : AssetDatabase.GetAssetPath(m_SelectedProfile);

            // 프로필 빌드도 -buildTarget을 넘겨야 Unity가 처음부터 그 플랫폼으로 열린다 (배치 모드는 도중 전환하면 실패)
            // Jenkins는 BuildJenkinsfileText가 만든 profileTarget(프로필 경로 → 플랫폼 표에서 고른 값)을 쓴다
            string profileTarget = isJenkinsVar
                ? "${profileTarget}"
                : m_SelectedProfile.GetResolvedBuildTarget().ToString();

            // 인자 하나(이름 + 값)가 한 항목이다. Jenkinsfile은 항목 사이에서만 줄을 바꾼다 (값 안의 " -"는 건드리지 않음)
            var list = new List<string>();

            // Jenkins는 BuildJenkinsfileText가 에이전트 환경(UNITY_PATH 또는 ProjectVersion.txt)으로 찾은 unityPath를 쓴다
            exe = isJenkinsVar ? "${unityPath}" : EditorApplication.applicationPath;
            list.Add("-batchmode");
            list.Add("-quit");
            list.Add($"-projectPath \"{projectPath}\"");
            list.Add("-executeMethod HB.CustomBuild.Editor.ProjectBuild.JenkinsBuild");
            list.Add($"-logFile \"{logPath}\"");
            list.Add($"-buildTarget {profileTarget}");
            list.Add($"-{BuildHelper.BUILD_PROFILE} \"{profilePath}\"");

            argList = list;
        }

        private void ExportBatchScript(string cmd)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string selected = EditorUtility.SaveFilePanel("배치 스크립트(.bat) 저장", projectRoot, "RunBuild", "bat");
            if (!string.IsNullOrEmpty(selected))
            {
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("setlocal");
                // 한글 경로가 깨지지 않게 UTF-8로 쓰고 코드 페이지를 맞춘다 (BOM이 있으면 cmd가 첫 줄을 명령으로 못 읽음)
                // 열려 있던 cmd 창에서 실행해도 끝나면 원래 코드 페이지로 돌아가게 저장해 둔다 (setlocal은 chcp를 되돌리지 않음)
                sb.AppendLine("for /f \"tokens=2 delims=:.\" %%a in ('chcp') do set \"HB_OLD_CP=%%a\"");
                sb.AppendLine("chcp 65001 >nul");
                sb.AppendLine("echo ===================================================");
                sb.AppendLine("echo   Starting Automated Unity CLI Build Process");
                sb.AppendLine("echo ===================================================");
                sb.AppendLine(cmd);
                sb.AppendLine("set \"HB_EXIT=%ERRORLEVEL%\"");
                sb.AppendLine("echo.");
                sb.AppendLine("if %HB_EXIT% EQU 0 (");
                sb.AppendLine("    echo [SUCCESS] Unity Build finished with success code 0.");
                sb.AppendLine(") else (");
                sb.AppendLine("    echo [ERROR] Unity Build failed! Check build.log for details.");
                sb.AppendLine(")");
                sb.AppendLine("if defined HB_OLD_CP chcp %HB_OLD_CP% >nul");
                sb.AppendLine("pause");

                File.WriteAllText(selected, sb.ToString(), new UTF8Encoding(false));
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
                BuildCommandParts(false, out string exe, out string args);

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

        private void DrawLocalGitCleanOption()
        {
            bool enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("빌드 전 git 작업 폴더 정리 (로컬 빌드만)", "Unity 프로젝트 폴더를 현재 커밋 상태로 되돌린 뒤 빌드합니다. 산출물 이름에 -dirty가 붙지 않습니다."),
                m_LocalGitClean);
            if (enabled != m_LocalGitClean)
            {
                m_LocalGitClean = enabled;
                m_LocalGitCleanConfirmed = false;
            }
            if (!m_LocalGitClean) return;

            EditorGUILayout.HelpBox(
                "빌드 전에 Unity 프로젝트 폴더의 커밋하지 않은 수정 · 스테이징과 추적하지 않는 파일을 지웁니다. 되돌릴 수 없습니다.\n" +
                "남는 것: Library 등 .gitignore 대상, 빌드 산출물 폴더, 프로젝트 밖 파일.\n" +
                "Play 모드이거나 저장하지 않은 씬 · 에셋이 있으면 정리하지 않습니다 (먼저 저장하거나 되돌리세요).\n" +
                "빌드 프로필은 빌드할 때 저장되지만, 커밋하지 않은 프로필 변경은 정리로 되돌아갑니다 (커밋 전 새 프로필은 삭제).\n" +
                "정리 직전에 지울 목록을 한 번 더 보여 줍니다. 스크립트가 바뀌면 정리만 하고, 컴파일이 끝난 뒤 빌드를 다시 눌러야 합니다.",
                MessageType.Error);
            m_LocalGitCleanConfirmed = EditorGUILayout.ToggleLeft("위 파일들이 지워지는 것을 확인했습니다", m_LocalGitCleanConfirmed);
        }

        private void DrawActionButtons()
        {
            Color prevBackground = GUI.backgroundColor;

            DrawLocalGitCleanOption();
            EditorGUILayout.Space(4);

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

        /// <summary>
        /// 로컬 빌드 전 git 작업 폴더 정리. 지울 목록을 Console과 대화상자로 보이고 확인을 받은 뒤 정리한다.
        /// 스크립트 · 패키지 · 프로젝트 설정이 바뀌면 컴파일(도메인 리로드)이 빌드 호출을 끊으므로 빌드하지 않고 false.
        /// </summary>
        private bool CleanWorkingFolderBeforeBuild()
        {
            var clean = LocalGitClean.Prepare(out string error);
            if (clean == null)
            {
                EditorUtility.DisplayDialog("git 작업 폴더 정리 불가", error + "\n\n빌드하지 않았습니다.", "확인");
                return false;
            }

            if (clean.Count == 0)
            {
                BuildHelper.Log("[LocalClean] Nothing to clean. The project folder already matches the current commit.");
                m_LocalGitClean = m_LocalGitCleanConfirmed = false;
                return true;
            }

            BuildHelper.LogWarning("[LocalClean] Files to discard before the build:\n" + clean.DescribeForConsole());
            if (!EditorUtility.DisplayDialog("git 작업 폴더 정리",
                    clean.Describe(20) + "\n\n이 파일들을 지우고 현재 커밋 상태로 빌드합니다. 되돌릴 수 없습니다.",
                    "정리 후 빌드", "취소"))
            {
                return false;
            }

            // 대화상자가 떠 있는 동안 파일이 바뀌었으면 확인한 목록과 다르므로 실행하지 않는다
            var recheck = LocalGitClean.Prepare(out error);
            if (!clean.SameAs(recheck))
            {
                EditorUtility.DisplayDialog("git 작업 폴더 정리 중단",
                    (recheck == null ? error + "\n\n" : "확인하는 동안 지울 목록이 바뀌었습니다.\n\n") + "정리 · 빌드를 하지 않았습니다. 빌드를 다시 눌러 새 목록을 확인하세요.", "확인");
                return false;
            }

            bool changesScripts = clean.ChangesScripts;
            bool cleaned = clean.Run(out error);
            m_LocalGitClean = m_LocalGitCleanConfirmed = false;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!cleaned)
            {
                EditorUtility.DisplayDialog("git 작업 폴더 정리 실패", error + "\n\n빌드하지 않았습니다. Console의 목록과 git 상태를 확인하세요.", "확인");
                return false;
            }
            BuildHelper.Log($"[LocalClean] Cleaned {clean.Count} item(s).");

            if (changesScripts || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("정리 완료 · 빌드하지 않음",
                    "스크립트 · 패키지 · 프로젝트 설정이 바뀌어 컴파일이 필요합니다.\n컴파일이 끝나면 빌드를 다시 누르세요.", "확인");
                return false;
            }
            return true;
        }

        /// <summary> 이번 빌드가 쓰는 설정 에셋 (프로필의 참조). 정리로 지워졌는지 보려고 정리 전에 기록한다 </summary>
        private List<(string Label, UnityEngine.Object Asset)> GetReferencedSettingAssets()
        {
            var list = new List<(string Label, UnityEngine.Object Asset)>();
            if (m_SelectedProfile.SceneDefineAsset != null) list.Add(("SceneDefine", m_SelectedProfile.SceneDefineAsset));
            if (m_SelectedProfile.ProjectDefineAsset != null) list.Add(("ProjectDefine", m_SelectedProfile.ProjectDefineAsset));
            if (m_SelectedProfile.XcodeSettingsAsset != null) list.Add(("XcodeSettings", m_SelectedProfile.XcodeSettingsAsset));
            return list;
        }

        /// <summary> LIVE + Development는 라이브 환경 확인용으로 허용하되, 실수로 켠 것이 아닌지 한 번 묻는다 </summary>
        private static bool ConfirmLiveDevBuild(BuildContext context)
        {
            return !context.IsLiveDevBuild || EditorUtility.DisplayDialog(
                "LIVE + Development Build",
                "LIVE 빌드인데 Development Build가 켜져 있습니다" + (context.IsDeepProfile ? " (Deep Profiling 포함)" : string.Empty) + ".\n\n" +
                "라이브 환경 프로파일링 · 로그 확인용이 아니라면 끄고 빌드하세요. 산출물 이름에 _DEVBUILD가 붙습니다.",
                "그대로 빌드", "취소");
        }

        private void ExecuteLocalBuild()
        {
            // delayCall 시점에 창이 닫혔을 수 있다
            if (this == null) return;

            if (m_SelectedProfile == null) return;

            string buildName = $"'{m_SelectedProfile.name}' 프로필 기반 ";

            try
            {
                var context = m_SelectedProfile.ToBuildContext();

                if (!ConfirmLiveDevBuild(context))
                    return;

                // 창의 편집은 프로필에 바로 들어가 있다. 빌드한 값과 디스크의 프로필(Jenkins가 읽는 값)이 같도록 빌드 전에 저장한다
                // (git 정리는 저장 안 한 에셋이 있으면 거부하므로 정리 전에. 커밋하지 않은 저장은 정리로 되돌아간다 — 정리 목록 · 경고에 나옴)
                AssetDatabase.SaveAssetIfDirty(m_SelectedProfile);

                // 되돌릴 수 없는 정리는 다른 확인을 모두 지난 뒤에 한다
                if (m_LocalGitClean)
                {
                    if (!m_LocalGitCleanConfirmed)
                    {
                        EditorUtility.DisplayDialog("git 작업 폴더 정리",
                            "'빌드 전 git 작업 폴더 정리'가 켜져 있지만 확인 체크박스가 꺼져 있습니다.\n\n" +
                            "정리하려면 경고 아래의 확인 체크박스를 켜고, 정리하지 않으려면 정리 옵션을 끄세요.", "확인");
                        return;
                    }
                    bool wasLiveDev = context.IsLiveDevBuild;
                    var referenced = GetReferencedSettingAssets();
                    if (!CleanWorkingFolderBeforeBuild())
                        return;

                    // 커밋 전 프로필이었으면 정리로 지워진다
                    if (m_SelectedProfile == null)
                    {
                        EditorUtility.DisplayDialog("빌드하지 않음",
                            "선택한 프로필이 커밋되지 않은 파일이라 정리로 지워졌습니다.\n프로필을 다시 고른 뒤 빌드하세요.", "확인");
                        return;
                    }
                    // 참조하던 SceneDefine · ProjectDefine · XcodeSettings가 커밋 전이라 지워졌으면 그 스텝이 조용히 빠지므로 멈춘다
                    var removed = referenced.Where(r => r.Asset == null).Select(r => r.Label).ToList();
                    if (removed.Count > 0)
                    {
                        EditorUtility.DisplayDialog("빌드하지 않음",
                            $"참조하던 설정 에셋이 커밋되지 않은 파일이라 정리로 지워졌습니다: {string.Join(", ", removed)}\n에셋을 커밋하거나 다시 지정한 뒤 빌드하세요.", "확인");
                        return;
                    }
                    // 정리로 프로필 · 설정 에셋이 디스크에서 바뀌었을 수 있으므로 다시 만들고, 새로 LIVE + Development가 됐으면 다시 묻는다
                    m_ProfileSO = null;
                    context = m_SelectedProfile.ToBuildContext();
                    if (!wasLiveDev && !ConfirmLiveDevBuild(context))
                        return;
                }

                BuildHelper.Log($"[Pipeline] Executing build from Profile: {m_SelectedProfile.name} (Platform: {m_SelectedProfile.TargetPlatform}, Mode: {m_SelectedProfile.BuildType})");
                AbsBuild.Execute(context);

                EditorUtility.DisplayDialog("빌드 완료", $"{buildName}플레이어 빌드가 성공적으로 완료되었습니다!" +
                    (context.Warnings.Count > 0 ? $"\n\n경고 {context.Warnings.Count}건이 있습니다. Console 또는 {AbsBuild.WARNINGS_FILE}를 확인하세요." : string.Empty), "확인");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("빌드 실패", $"{buildName}플레이어 빌드에 실패했습니다.\n\n{ex.Message}\n\n자세한 내용은 Console을 확인하세요.", "확인");
            }
        }

        private void GenerateJenkinsfile()
        {
            if (m_JenkinsCheckout == EJenkinsCheckout.LocalFolder && !TryGetJenkinsProjectDir(out _, out string dirError))
            {
                EditorUtility.DisplayDialog("Jenkinsfile 생성 불가", dirError, "확인");
                return;
            }

            string groovyCode = BuildJenkinsfileText();
            GUIUtility.systemCopyBuffer = groovyCode;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string jenkinsfilePath = Path.Combine(projectRoot, "Jenkinsfile");

            bool saveFile = EditorUtility.DisplayDialog(
                "Jenkinsfile (Groovy) 생성 완료",
                "Jenkinsfile Groovy 코드가 클립보드에 복사되었습니다!\n\n프로젝트 루트에 'Jenkinsfile' 파일로 저장하시겠습니까?",
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

        /// <summary>
        /// 프로젝트의 프로필 목록(선택한 프로필이 첫 항목)과 창의 Jenkins 옵션으로 Jenkinsfile(Groovy) 내용을 만듭니다.
        /// </summary>
        private string BuildJenkinsfileText()
        {
            // 프로필 경로 → 플랫폼 (생성 시점 값. 프로필 플랫폼을 바꾸면 다시 생성해야 한다)
            var profileTargets = new Dictionary<string, BuildTarget>
            {
                [AssetDatabase.GetAssetPath(m_SelectedProfile)] = m_SelectedProfile.GetResolvedBuildTarget()
            };
            foreach (var p in HBBuildProfile.FindAllProfiles())
            {
                string pPath = AssetDatabase.GetAssetPath(p);
                if (!profileTargets.ContainsKey(pPath))
                    profileTargets[pPath] = p.GetResolvedBuildTarget();
            }
            var profileChoices = profileTargets.Keys.ToList();

            string choicesArrayStr = string.Join(", ", profileChoices.Select(GroovyQuote));
            string defaultProfile = profileChoices[0];

            // 로컬 폴더 모드: 모든 단계가 빌드 PC의 작업 폴더(PROJECT_DIR) 기준. SCM 모드 출력은 바꾸지 않는다
            bool localFolder = m_JenkinsCheckout == EJenkinsCheckout.LocalFolder;

            var sb = new StringBuilder();
            sb.AppendLine("// ==========================================================================");
            sb.AppendLine($"// Jenkins Declarative Pipeline - Generated by HB Custom Build Manager");
            if (localFolder)
                sb.AppendLine("// Checkout: LocalFolder (fixed build PC folder, git fetch + checkout -f). Paste into a 'Pipeline script' job, one folder per job. To change the folder, regenerate.");
            sb.AppendLine($"// Generated Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("// ==========================================================================");
            sb.AppendLine("pipeline {");
            // 로컬 폴더 모드는 그 폴더가 있는 노드에서만 돌아야 한다
            string agentLabel = m_JenkinsAgentLabel.Trim();
            if (localFolder && agentLabel.Length > 0)
                sb.AppendLine($"    agent {{ label {GroovyQuote(agentLabel)} }}");
            else
                sb.AppendLine("    agent any");
            sb.AppendLine();
            // 체크아웃은 아래 'Checkout SCM' 단계에서 한 번만 한다 (Declarative 기본 checkout과 중복 방지)
            sb.AppendLine("    options {");
            sb.AppendLine("        skipDefaultCheckout()");
            // 같은 작업 폴더를 두 빌드가 동시에 쓰지 않게 한다 (다른 job이 같은 폴더를 쓰는 것은 막지 못함 → job마다 폴더 하나)
            if (localFolder)
                sb.AppendLine("        disableConcurrentBuilds()");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    parameters {");

            sb.AppendLine($"        choice(name: 'BUILD_PROFILE', choices: [{choicesArrayStr}], description: '실행할 HBBuildProfile(.asset) 경로 (생성한 PC에 있던 프로필)')");
            // 목록에 없는 프로필(빌드 PC 전용 .gitignore 폴더 등)은 경로와 플랫폼을 직접 넣는다. 플랫폼이 틀리면 배치 빌드가 불일치로 실패한다
            sb.AppendLine("        string(name: 'BUILD_PROFILE_PATH', defaultValue: '', description: '목록에 없는 프로필 경로 직접 입력 (예: Assets/Editor/HBBuild/Profiles/BuildMachine/Live_AOS.asset). 비우면 BUILD_PROFILE 사용')");
            sb.AppendLine($"        choice(name: 'BUILD_TARGET', choices: ['{PROFILE_VALUE}', 'StandaloneWindows64', 'Android', 'iOS'], description: '프로필 플랫폼 ({PROFILE_VALUE} = 생성 시 기록된 플랫폼, 직접 입력한 프로필은 필수)')");
            // 기본은 프로필 값, 값을 넣은 파라미터만 프로필을 덮어쓴다 (덮어쓸 수 있는 것은 버전 · 버전 코드 · Development뿐)
            sb.AppendLine($"        choice(name: 'DEV_BUILD', choices: ['{PROFILE_VALUE}', 'true', 'false'], description: 'Development Build ({PROFILE_VALUE} = 프로필 값)')");
            sb.AppendLine("        string(name: 'BUILD_VERSION', defaultValue: '', description: '앱 버전 (Bundle Version, 비우면 프로필 값)')");
            sb.AppendLine("        string(name: 'VERSION_CODE', defaultValue: '', description: '버전 코드 (Android/iOS, 비우면 프로필 값)')");

            // 작업 폴더는 파라미터로 열지 않는다 (빌드 권한만으로 다른 git 폴더를 지정해 checkout -f · clean -fd 하지 못하게)
            if (localFolder)
            {
                sb.AppendLine($"        string(name: 'BRANCH', defaultValue: {GroovyQuote(m_JenkinsBranch.Trim())}, description: '빌드할 브랜치 (origin 기준). 비우면 작업 폴더의 현재 브랜치')");
                sb.AppendLine("        string(name: 'GIT_REF', defaultValue: '', description: '태그 또는 커밋 해시(7~40자리, 원격 브랜치 · 태그에 있는 것)로 빌드. 넣으면 BRANCH보다 우선')");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
            if (localFolder)
            {
                // PROJECT_PATH · OUTPUT_DIR · LOG_FILE은 'Update Source (git)' 단계가 폴더를 검사한 뒤 env에 넣는다
                // (environment 블록에 두면 스크립트에서 바꿀 수 없음)
                TryGetJenkinsProjectDir(out string projectDir, out _);
                sb.AppendLine("    stages {");
                AppendLocalFolderUpdateStage(sb, projectDir);
            }
            else
            {
                // UNITY_PATH는 넣지 않는다 (생성 PC의 경로가 박히면 에이전트 경로가 다르거나 Unity를 올릴 때 깨짐). 실행 시 찾는다
                sb.AppendLine("    environment {");
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
            }
            sb.AppendLine();
            // 이전 빌드 산출물만 지운다 (Library 캐시는 유지. 소스 정리는 SCM 모드면 job의 checkout 설정, 로컬 폴더 모드면 Update Source 단계)
            sb.AppendLine("        stage('Clean Build Output') {");
            sb.AppendLine("            steps {");
            sb.AppendLine("                dir(env.OUTPUT_DIR) {");
            sb.AppendLine("                    deleteDir()");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        stage('Unity Build Execution') {");
            sb.AppendLine("            steps {");
            // 단계 본문은 따로 모은다 (로컬 폴더 모드는 dir(PROJECT_PATH) 안에서 실행 — readFile 상대 경로 기준)
            var body = new StringBuilder();
            body.AppendLine("                script {");

            // Unity 경로: 에이전트 환경변수 UNITY_PATH, 없으면 ProjectVersion.txt의 버전으로 Hub 설치 경로(UNITY_EDITORS_ROOT로 바꿀 수 있음)
            body.AppendLine("                    def unityPath = env.UNITY_PATH");
            body.AppendLine("                    if (!unityPath) {");
            body.AppendLine("                        def versionLine = readFile('ProjectSettings/ProjectVersion.txt').readLines().find { it.startsWith('m_EditorVersion:') }");
            body.AppendLine("                        if (!versionLine) error('m_EditorVersion not found in ProjectSettings/ProjectVersion.txt')");
            body.AppendLine("                        def editorVersion = versionLine.substring('m_EditorVersion:'.length()).trim()");
            body.AppendLine("                        if (isUnix()) {");
            body.AppendLine("                            unityPath = \"${env.UNITY_EDITORS_ROOT ?: '/Applications/Unity/Hub/Editor'}/${editorVersion}/Unity.app/Contents/MacOS/Unity\"");
            body.AppendLine("                        } else {");
            body.AppendLine("                            unityPath = \"${env.UNITY_EDITORS_ROOT ?: 'C:/Program Files/Unity/Hub/Editor'}/${editorVersion}/Editor/Unity.exe\"");
            body.AppendLine("                        }");
            body.AppendLine("                    }");
            body.AppendLine("                    if (!fileExists(unityPath)) error(\"Unity editor not found: ${unityPath} (set UNITY_PATH or UNITY_EDITORS_ROOT on the agent)\")");
            body.AppendLine("                    echo \"Unity: ${unityPath}\"");
            body.AppendLine();

            // 고른 프로필의 플랫폼을 -buildTarget으로 넘긴다 (생성 시점의 프로필 플랫폼. 바꿨으면 다시 생성 — 배치 빌드가 불일치로 실패함)
            string targetMap = string.Join(", ", profileTargets.Select(kv => $"{GroovyQuote(kv.Key)}: {GroovyQuote(kv.Value.ToString())}"));
            body.AppendLine($"                    def profileTargets = [{targetMap}]");
            body.AppendLine("                    def profilePath = params.BUILD_PROFILE_PATH?.trim() ?: params.BUILD_PROFILE");
            // 명령줄에 들어가므로 에셋 경로에 쓰는 문자만 허용한다
            body.AppendLine(@"                    if (!(profilePath ==~ /[\p{L}\p{N} ._\/-]+\.asset/)) error(""Profile path must be an .asset path (letters, digits, space, . _ / -): ${profilePath}"")");
            body.AppendLine($"                    def profileTarget = params.BUILD_TARGET && params.BUILD_TARGET != '{PROFILE_VALUE}' ? params.BUILD_TARGET : profileTargets[profilePath]");
            body.AppendLine("                    if (!profileTarget) error(\"No build target recorded for profile ${profilePath}. Choose BUILD_TARGET or regenerate the Jenkinsfile.\")");
            body.AppendLine();

            // 자유 입력 파라미터는 sh/bat 명령줄에 들어가므로 허용 문자만 통과시킨다 ($(...), ", & 등으로 명령이 깨지거나 끼어드는 것 방지)
            // 첫 글자 '-'도 막는다 (GetArg가 '-'로 시작하는 값을 다음 인자 이름으로 보고 버림)
            body.AppendLine("                    ['BUILD_VERSION', 'VERSION_CODE'].each { name ->");
            body.AppendLine("                        def value = params[name]?.trim()");
            body.AppendLine("                        if (value && !(value ==~ /[0-9A-Za-z._][0-9A-Za-z._-]*/)) {");
            body.AppendLine("                            error(\"${name} contains characters not allowed (allowed: 0-9 A-Z a-z . _ -, not starting with -)\")");
            body.AppendLine("                        }");
            body.AppendLine("                    }");
            body.AppendLine();

            BuildCommandParts(true, out string jenkinsExe, out List<string> jenkinsArgs);
            string formattedCmd = string.Join(" \\\n                        ", new[] { $"\"{jenkinsExe}\"" }.Concat(jenkinsArgs));

            body.AppendLine("                    def unityArgs = \"\"\"");
            body.AppendLine($"                        {formattedCmd}");
            body.AppendLine("                    \"\"\".stripIndent().trim()");
            body.AppendLine();

            // 빈 값을 인자로 넘기면 빌드가 실패하므로(-buildVersion "" 등) 값을 넣은 파라미터만 인자로 붙인다
            body.AppendLine("                    def overrides = []");
            body.AppendLine($"                    if (params.DEV_BUILD && params.DEV_BUILD != '{PROFILE_VALUE}') overrides << \"-{BuildHelper.DEV_BUILD} ${{params.DEV_BUILD}}\"");
            body.AppendLine($"                    if (params.BUILD_VERSION?.trim()) overrides << \"-{BuildHelper.BUILD_VERSION} \\\"${{params.BUILD_VERSION.trim()}}\\\"\"");
            body.AppendLine($"                    if (params.VERSION_CODE?.trim()) overrides << \"-{BuildHelper.VERSION_CODE} \\\"${{params.VERSION_CODE.trim()}}\\\"\"");
            body.AppendLine("                    if (overrides) {");
            body.AppendLine("                        unityArgs += ' ' + overrides.join(' ')");
            body.AppendLine("                    }");
            body.AppendLine("                    echo \"Profile overrides: ${overrides ? overrides.join(' ') : '(none)'}\"");
            body.AppendLine();
            body.AppendLine("                    if (isUnix()) {");
            body.AppendLine("                        sh unityArgs");
            body.AppendLine("                    } else {");
            body.AppendLine("                        bat unityArgs");
            body.AppendLine("                    }");
            body.AppendLine("                }");
            AppendInProjectDir(sb, body.ToString(), localFolder, "                ");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        stage('Archive Artifacts') {");
            sb.AppendLine("            steps {");
            if (localFolder)
            {
                // 산출물 옆에 빌드한 커밋을 남긴다 (Update Source 단계가 env에 넣은 값)
                sb.AppendLine("                dir(env.PROJECT_PATH) {");
                sb.AppendLine("                    writeFile file: 'BuildOutput/BUILD_COMMIT.txt', text: \"commit=${env.HB_GIT_COMMIT}\\nref=${env.HB_GIT_REF}\\n\"");
                sb.AppendLine("                    archiveArtifacts artifacts: 'BuildOutput/**', allowEmptyArchive: true");
                sb.AppendLine("                }");
            }
            else
            {
                sb.AppendLine("                archiveArtifacts artifacts: 'BuildOutput/**', allowEmptyArchive: true");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    post {");
            sb.AppendLine("        always {");
            // HB_Build가 남긴 경고(LIVE + Development, 심볼 항목 없음 등)가 있으면 내용을 출력하고 UNSTABLE로 표시한다
            var always = new StringBuilder();
            always.AppendLine($"if (fileExists('{AbsBuild.WARNINGS_FILE}')) {{");
            always.AppendLine($"    echo \"HB_Build warnings:\\n\" + readFile('{AbsBuild.WARNINGS_FILE}')");
            always.AppendLine("    unstable('HB_Build reported warnings (see the log above)')");
            always.AppendLine("}");
            if (localFolder)
            {
                // PROJECT_DIR 검사 전에 실패했으면 볼 폴더가 없다 (dir()은 없는 폴더를 만들어 버림)
                sb.AppendLine("            script {");
                sb.AppendLine("                if (env.HB_PROJECT_READY == 'true') {");
                sb.AppendLine("                    dir(env.PROJECT_PATH) {");
                AppendIndented(sb, always.ToString(), "                        ");
                sb.AppendLine("                        archiveArtifacts artifacts: 'build.log', allowEmptyArchive: true");
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
            else
            {
                sb.AppendLine("            script {");
                AppendIndented(sb, always.ToString(), "                ");
                sb.AppendLine("            }");
                sb.AppendLine("            archiveArtifacts artifacts: 'build.log', allowEmptyArchive: true");
            }
            sb.AppendLine("        }");
            sb.AppendLine("        success {");
            sb.AppendLine("            echo 'Unity Build Completed Successfully!'");
            sb.AppendLine("        }");
            sb.AppendLine("        failure {");
            sb.AppendLine("            echo 'Unity Build Failed. Check build.log for details.'");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        // 줄 앞에 indent를 붙인다. 줄바꿈 문자는 그대로 둔다 (명령줄 이어쓰기 '\'+LF가 섞여 있음)
        private static void AppendIndented(StringBuilder sb, string text, string indent)
        {
            sb.Append(Regex.Replace(text, "(?m)^(?=[^\r\n])", indent));
        }

        // 로컬 폴더 모드면 단계 본문을 dir(PROJECT_PATH) 안에 넣는다. 아니면 그대로 (SCM 모드 출력 유지)
        private static void AppendInProjectDir(StringBuilder sb, string steps, bool inProjectDir, string indent)
        {
            if (!inProjectDir)
            {
                sb.Append(steps);
                return;
            }
            sb.AppendLine(indent + "dir(env.PROJECT_PATH) {");
            AppendIndented(sb, steps, "    ");
            sb.AppendLine(indent + "}");
        }

        /// <summary>
        /// 로컬 폴더 모드의 빌드 PC 폴더를 검사한다. 끝의 \ · /는 뗀다 (-projectPath "...\"의 따옴표가 깨짐).
        /// 명령줄에 들어가므로 경로에 쓰는 문자만 허용하고, 네트워크 경로(UNC)와 .. 구간은 거부한다.
        /// </summary>
        private bool TryGetJenkinsProjectDir(out string dir, out string error)
        {
            dir = (m_JenkinsProjectDir ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (dir.Length == 0)
                error = "로컬 폴더 모드는 '빌드 PC 프로젝트 폴더'가 필요합니다.";
            else if (dir.StartsWith(@"\\") || dir.StartsWith("//"))
                error = $"네트워크 경로(UNC)는 쓸 수 없습니다: {dir}";
            else if (!Regex.IsMatch(dir, @"^[\p{L}\p{N} ._:\\/()-]+$"))
                error = $"빌드 PC 프로젝트 폴더에 쓸 수 없는 문자가 있습니다 (글자 · 숫자 · 공백 · . _ : \\ / ( ) - 만): {dir}";
            else if (Regex.IsMatch(dir, @"(^|[\\/])\.\.([\\/]|$)"))
                error = $"빌드 PC 프로젝트 폴더에 .. 구간은 쓸 수 없습니다: {dir}";
            else
                error = null;
            return error == null;
        }

        /// <summary>
        /// 로컬 폴더 모드의 소스 갱신 단계. 빌드 PC의 작업 폴더(생성 시 고정)를 원격 커밋에 강제로 맞춘다.
        /// pull 대신 fetch + checkout -f: 충돌 없이 항상 원격과 같고, 이전 빌드가 바꾼 파일도 버린다.
        /// clean은 -x 없이 (Library · 무시된 빌드 PC 전용 파일 유지). 버려지는 것은 먼저 로그에 남긴다.
        /// ref 값은 git 명령줄에 들어가므로 ref에 쓰는 문자만 허용하고 따옴표 없이 쓴다 (cmd의 ^ 처리를 피함).
        /// </summary>
        private static void AppendLocalFolderUpdateStage(StringBuilder sb, string projectDir)
        {
            AppendIndented(sb, LOCAL_FOLDER_UPDATE_STAGE.Replace(PROJECT_DIR_PLACEHOLDER, GroovyQuote(projectDir)), "        ");
        }

        private const string PROJECT_DIR_PLACEHOLDER = "__HB_PROJECT_DIR__";

        private const string LOCAL_FOLDER_UPDATE_STAGE = @"stage('Update Source (git)') {
    steps {
        script {
            // 작업 폴더는 생성 시 고정 (빌드 파라미터로 열지 않음). 바꾸려면 Jenkinsfile을 다시 생성한다
            def projectDir = __HB_PROJECT_DIR__
            if (!(projectDir ==~ /[\p{L}\p{N} ._:\\\/()-]+/)) error(""Project folder contains characters not allowed (letters, digits, space, . _ : \\ / ( ) -): ${projectDir}"")
            if (!fileExists(""${projectDir}/ProjectSettings/ProjectVersion.txt"") || !fileExists(""${projectDir}/.git"")) {
                error(""Project folder is not a Unity project git working copy: ${projectDir}. Prepare it once by hand (git clone, git lfs install, open in Unity), then run again."")
            }
            def branch = params.BRANCH?.trim()
            def gitRef = params.GIT_REF?.trim()
            def checkRef = { String name, String value ->
                if (value && (!(value ==~ /[0-9A-Za-z._\/-]+/) || value.startsWith('-') || value.contains('..'))) {
                    error(""${name} contains characters not allowed (0-9 A-Z a-z . _ / -, not starting with -, no ..): ${value}"")
                }
            }
            checkRef('BRANCH', branch)
            checkRef('GIT_REF', gitRef)

            env.PROJECT_PATH = projectDir
            env.OUTPUT_DIR = ""${projectDir}/BuildOutput""
            env.LOG_FILE = ""${projectDir}/build.log""

            dir(projectDir) {
                def gitRun = { String cmd -> isUnix() ? sh(cmd) : bat(cmd) }
                def gitStatus = { String cmd -> isUnix() ? sh(script: cmd, returnStatus: true) : bat(script: ""@${cmd}"", returnStatus: true) }
                // bat은 명령 줄도 출력에 섞이므로 @로 끈다
                def gitOut = { String cmd -> (isUnix() ? sh(script: cmd, returnStdout: true) : bat(script: ""@${cmd}"", returnStdout: true)).trim() }

                gitRun('git lfs version')
                // 첫 준비를 다른 계정으로 했어도 이 저장소에 LFS 필터가 걸리게 한다 (전역 설정에만 있으면 서비스 계정에는 없음)
                gitRun('git lfs install --local')

                // 강제 종료된 이전 빌드가 남긴 lock. job마다 폴더 하나 + 동시 실행 금지라 오래된 것은 찌꺼기로 본다
                if (fileExists('.git/index.lock')) {
                    def stale
                    if (isUnix()) {
                        stale = gitOut('find .git/index.lock -mmin +10')
                    } else {
                        stale = gitOut('powershell -NoProfile -Command ""if (((Get-Date) - (Get-Item .git/index.lock).LastWriteTime).TotalMinutes -gt 10) { Write-Output stale }""')
                    }
                    if (!stale) error('.git/index.lock is less than 10 minutes old. Another git process may be running in the project folder. Check and run again.')
                    echo 'WARNING: removing stale .git/index.lock (older than 10 minutes, left by an interrupted git process)'
                    gitRun(isUnix() ? 'rm -f .git/index.lock' : 'del /f /q .git\\index.lock')
                }

                // --prune-tags: 원격에서 지운 태그를 로컬에서도 지운다 (지워진 태그로 빌드되지 않게)
                gitRun('git fetch origin --prune --prune-tags --tags --force')

                def target
                if (gitRef) {
                    // 태그, 또는 원격 브랜치 · 태그에 있는 커밋 해시만 받는다
                    // (refs/heads/x · origin/x · HEAD 같은 이름으로 로컬의 오래된 상태를 빌드하지 않게)
                    if (gitStatus(""git show-ref --verify --quiet refs/tags/${gitRef}"") == 0) {
                        // 태그는 가리키는 커밋으로 푼다
                        target = gitOut(""git rev-list -n 1 refs/tags/${gitRef} --"")
                    } else if ((gitRef ==~ /[0-9a-fA-F]{7,40}/) && gitStatus(""git rev-parse --verify --quiet ${gitRef}"") == 0) {
                        target = gitOut(""git rev-list -n 1 ${gitRef} --"")
                        if (!gitOut(""git branch -r --contains ${target}"") && !gitOut(""git tag --contains ${target}"")) {
                            error(""GIT_REF commit ${target} is not on any remote branch or tag (local-only commit)."")
                        }
                    } else {
                        error(""GIT_REF must be a tag or a 7-40 digit commit hash on the remote: ${gitRef}. Use BRANCH for branches."")
                    }
                } else {
                    if (!branch) {
                        if (gitStatus('git symbolic-ref --short -q HEAD') != 0) error('HEAD is detached (an earlier build used GIT_REF). Set BRANCH.')
                        branch = gitOut('git symbolic-ref --short -q HEAD')
                        // git에서 얻은 이름도 명령줄에 들어가므로 같은 검사를 한다
                        checkRef('BRANCH', branch)
                    }
                    if (gitStatus(""git show-ref --verify --quiet refs/remotes/origin/${branch}"") != 0) error(""Branch not found on origin: ${branch}"")
                }

                // 버려질 것을 남긴다: 작업 폴더 수정, 어느 원격에도 없는 커밋(HEAD와 로컬 브랜치 — checkout -B가 덮음), 추적되지 않는 파일
                echo 'Local changes to discard (git status --porcelain):'
                gitRun('git status --porcelain')
                echo 'Commits on no remote (left behind):'
                gitRun('git log --oneline HEAD --branches --not --remotes')
                echo 'Untracked files to remove (git clean -nd):'
                gitRun('git clean -nd')

                if (gitRef) {
                    gitRun(""git checkout -f --detach ${target}"")
                } else {
                    gitRun(""git checkout -f -B ${branch} origin/${branch}"")
                }
                // -x 없이: Library · 무시된 빌드 PC 전용 파일은 남긴다
                gitRun('git clean -fd')
                if (fileExists('.gitmodules')) {
                    gitRun('git submodule sync --recursive')
                    gitRun('git submodule update --init --recursive --force')
                    gitRun('git submodule foreach --recursive ""git reset --hard && git clean -fd""')
                    gitRun('git submodule foreach --recursive git lfs pull')
                }
                gitRun('git lfs pull')

                def dirty = gitOut('git status --porcelain')
                if (dirty) error(""Working tree is not clean after the update (line endings, LFS, or a leftover submodule folder?). Fix the repository instead of loosening this check:\n${dirty}"")

                env.HB_GIT_COMMIT = gitOut('git rev-parse HEAD')
                env.HB_GIT_REF = gitRef ?: branch
                currentBuild.description = ""${env.HB_GIT_REF} @ ${gitOut('git rev-parse --short HEAD')}""
                echo ""Building ${env.HB_GIT_REF} @ ${env.HB_GIT_COMMIT}""
            }
            env.HB_PROJECT_READY = 'true'
        }
    }
}
";

        #region Jenkins CLI Entrypoint

        public static void JenkinsBuild()
        {
            BuildHelper.Log("=== Starting Jenkins CI/CD Automated Build ===");

            BuildHelper.SetUpAllArgs();
            // 인자 검사를 빌드 준비 전에 한다 (없앤 인자 · -devBuild 값 · 프로필 없음은 바로 실패)
            BuildHelper.ValidateArgs();
            string profileArg = BuildHelper.GetRequiredProfileArg();
            BuildHelper.ReadEnvironment();

            BuildHelper.Log($"Detected -buildProfile argument: {profileArg}");
            AbsBuild.ExecuteFromProfilePath(profileArg);
        }

        #endregion
    }
}
