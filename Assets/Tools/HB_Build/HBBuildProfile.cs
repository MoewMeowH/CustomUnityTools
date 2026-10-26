using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// HB Custom Build Manager 전용 빌드 프로필 ScriptableObject입니다.
    /// 프로젝트 내에 복수의 프로필(.asset)을 생성하여(예: PC_Dev, Android_Live_AAB 등)
    /// 플랫폼, 환경, 버전, 씬, 전처리기 에셋 등의 빌드 설정을 독립적으로 관리하고
    /// Jenkins CI/CD에서 '-buildProfile' 단일 파라미터로 재현성 높은 빌드를 수행할 수 있습니다.
    /// </summary>
    #if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
    #endif
    [CreateAssetMenu(fileName = "BuildProfile_New", menuName = "HB_Build/Build Profile", order = 50)]
    public class HBBuildProfile : ScriptableObject
    {
        [Header("=== 기본 타겟 및 모드 ===")]
        [Tooltip("빌드 타겟 플랫폼 (PC, Android, iOS)")]
        [SerializeField] public ProjectBuild.ESelfBuildPlatform TargetPlatform = ProjectBuild.ESelfBuildPlatform.PC;

        [Tooltip("빌드 환경 모드 (DEV, QA, LIVE)")]
        [SerializeField] public EBuildType BuildType = EBuildType.DEV;

        [Header("=== 버전 정보 ===")]
        [Tooltip("앱 버전 문자열 (Bundle Version)")]
        [SerializeField] public string Version = "1.0.0";

        [Tooltip("빌드 번호 / 버전 코드 (Android versionCode / iOS buildNumber)")]
        [SerializeField] public string VersionCode = string.Empty;

        [Header("=== 빌드 옵션 ===")]
        [Tooltip("Development Build 활성화 여부")]
        [SerializeField] public bool DevBuild = false;

        [Tooltip("Deep Profiling 활성화 여부 (DevBuild 활성화 시 적용)")]
        [SerializeField] public bool DeepProfile = false;

        [Tooltip("Clean Build Cache 활성화 여부")]
        [SerializeField] public bool CleanBuild = false;

        [Tooltip("Android 전용: App Bundle(.aab) 빌드 여부 (false 시 .apk)")]
        [SerializeField] public bool IsAab = false;

        [Tooltip("PC 전용: 빌드 결과물 ZIP 압축 여부")]
        [SerializeField] public bool ZipPackage = false;

        [Tooltip("iOS 전용: Provisioning Profile UUID")]
        [SerializeField] public string ProvisionUUID = string.Empty;

        [Header("=== 경로 및 씬 ===")]
        [Tooltip("빌드 출력 디렉토리 (비워둘 시 BuildOutput/<Target> 기본 경로 사용)")]
        [SerializeField] public string OutputPath = string.Empty;

        [Tooltip("빌드에 포함할 씬 목록 (비워둘 시 Build Settings 활성 씬 사용)")]
        [SerializeField] public List<string> Scenes = new List<string>();

        [Header("=== 선택적 설정 에셋 ===")]
        [Tooltip("환경별 무시 씬(IgnoreScene) 설정 에셋")]
        [SerializeField] public SceneDefine SceneDefineAsset;

        [Tooltip("플랫폼/모드별 전처리기 심볼(Scripting Define Symbols) 설정 에셋")]
        [SerializeField] public ProjectDefine ProjectDefineAsset;

        [Tooltip("iOS 전용: 빌드 뒤 Xcode 프로젝트에 적용할 설정 에셋 (Info.plist · Capabilities · Build Settings 등)")]
        [SerializeField] public HBXcodeSettings XcodeSettingsAsset;

        [Serializable]
        public struct CustomStringEntry
        {
            public string Key;
            public string Value;
        }

        [Header("=== 추가 커스텀 데이터 ===")]
        [Tooltip("파이프라인 스텝(IPreBuildStep/IPostBuildStep)에 전달할 확장 Key-Value")]
        [SerializeField] public List<CustomStringEntry> CustomEntries = new List<CustomStringEntry>();

        [Header("=== 파이프라인 스텝 ===")]
        [Tooltip("이 프로필로 빌드할 때 제외할 스텝 ID (IPreBuildStep/IPostBuildStep.StepId). Build Manager 창의 체크박스로 편집")]
        [SerializeField] public List<string> DisabledStepIds = new List<string>();

        /// <summary>
        /// 프로필에 저장된 설정을 바탕으로 파이프라인 실행용 BuildContext 객체를 생성합니다.
        /// </summary>
        public BuildContext ToBuildContext()
        {
            var target = GetResolvedBuildTarget();
            var targetGroup = BuildHelper.GetTargetGroup(target);

            string outDir = string.IsNullOrWhiteSpace(OutputPath) 
                ? BuildHelper.GetDefaultOutputPath(target) 
                : OutputPath.Trim();

            // 공백 항목만 있는 경우도 비어 있는 것으로 보고 Build Settings 씬을 사용
            var sceneList = Scenes != null
                ? Scenes.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();
            if (sceneList.Count == 0)
                sceneList = BuildHelper.GetDefaultBuildScenes().ToList();

            var context = new BuildContext
            {
                Target = target,
                TargetGroup = targetGroup,
                BuildType = BuildType,
                Version = Version,
                VersionCode = VersionCode,
                IsDevBuild = DevBuild,
                // 저장된 값 그대로 넘긴다 (CLI가 -devBuild만 덮어써도 살아 있게). 개발 빌드가 아니면 AbsBuild.Execute가 끈다
                IsDeepProfile = DeepProfile,
                IsCleanBuild = CleanBuild,
                IsAab = IsAab,
                ZipPackage = ZipPackage,
                OutputPath = outDir,
                Scenes = sceneList
            };

            if (DisabledStepIds != null)
            {
                foreach (var stepId in DisabledStepIds)
                {
                    if (!string.IsNullOrWhiteSpace(stepId))
                        context.DisabledStepIds.Add(stepId);
                }
            }

            if (SceneDefineAsset != null)
                context.SetCustomData("SceneDefine", SceneDefineAsset);

            if (ProjectDefineAsset != null)
                context.SetCustomData("ProjectDefine", ProjectDefineAsset);

            // 깨진 참조(Missing)도 넘겨서 스텝이 실패시키게 한다 (== null이면 건너뛰어 기본 경로의 다른 에셋이 쓰일 수 있음)
            if (!ReferenceEquals(XcodeSettingsAsset, null))
                context.SetCustomData(BuildStep_XcodeSettings.CUSTOM_DATA_KEY, XcodeSettingsAsset);

            if (TargetPlatform == ProjectBuild.ESelfBuildPlatform.iOS && !string.IsNullOrWhiteSpace(ProvisionUUID))
            {
                context.SetCustomData("ProvisionUUID", ProvisionUUID.Trim());
            }

            if (CustomEntries != null)
            {
                foreach (var entry in CustomEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key))
                        context.SetCustomData(entry.Key, entry.Value);
                }
            }

            context.RebuildPlayerOptions();

            return context;
        }

        /// <summary>
        /// ESelfBuildPlatform 열거형을 Unity BuildTarget으로 변환합니다.
        /// </summary>
        public BuildTarget GetResolvedBuildTarget()
        {
            return TargetPlatform switch
            {
                ProjectBuild.ESelfBuildPlatform.PC => BuildTarget.StandaloneWindows64,
                ProjectBuild.ESelfBuildPlatform.Android => BuildTarget.Android,
                ProjectBuild.ESelfBuildPlatform.iOS => BuildTarget.iOS,
                _ => BuildTarget.StandaloneWindows64
            };
        }

        /// <summary>
        /// 프로젝트 내의 모든 BuildProfile 에셋 목록을 로드합니다.
        /// </summary>
        public static List<HBBuildProfile> FindAllProfiles()
        {
            var result = new List<HBBuildProfile>();
            string[] guids = AssetDatabase.FindAssets("t:HBBuildProfile");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(path);
                if (profile != null)
                    result.Add(profile);
            }
            return result;
        }

        /// <summary>
        /// Assets/Editor/HBBuild/Profiles 경로에 새로운 기본 프로필 에셋을 생성합니다.
        /// </summary>
        public static HBBuildProfile CreateNewProfileAsset(string profileName = "New_BuildProfile")
        {
            string folder = BuildHelper.USER_PROFILES_DIR;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string safeName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.asset");

            var profile = CreateInstance<HBBuildProfile>();
            profile.Version = Application.version;
            profile.Scenes = BuildHelper.GetDefaultBuildScenes().ToList();

            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[HBBuildProfile] Created new build profile at: {assetPath}");
            return profile;
        }
    }
}
