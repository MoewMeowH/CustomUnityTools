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
    /// iOS 서명 종류. 앞 셋은 Unity ProvisioningProfileType 이름 그대로(수동 서명, ProvisionUUID 필요),
    /// AutoSign은 UUID 없이 자동 서명 (PreBuildStep_IOSSettings.PROVISION_TYPE_AUTO_SIGN)
    /// </summary>
    public enum EProvisionType
    {
        Distribution,
        Development,
        Automatic,
        AutoSign,
    }

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

        [Tooltip("Development Build일 때 플레이어가 에디터 Profiler에 자동 연결")]
        [SerializeField] public bool AutoConnectProfiler = false;

        [Tooltip("iOS 전용: Provisioning Profile UUID")]
        [SerializeField] public string ProvisionUUID = string.Empty;

        [Tooltip("iOS 전용: 서명 종류. UUID가 있을 때 적용 (AutoSign은 UUID 없이 자동 서명)")]
        [SerializeField] public EProvisionType ProvisionType = EProvisionType.Distribution;

        [Header("=== 씬 ===")]
        [Tooltip("빌드에 포함할 씬 목록 (비워둘 시 Build Settings 활성 씬 사용)")]
        [SerializeField] public List<string> Scenes = new List<string>();

        [Header("=== 선택적 설정 에셋 ===")]
        [Tooltip("환경별 무시 씬(IgnoreScene) 설정 에셋")]
        [SerializeField] public SceneDefine SceneDefineAsset;

        [Tooltip("플랫폼/모드별 전처리기 심볼(Scripting Define Symbols) 설정 에셋")]
        [SerializeField] public ProjectDefine ProjectDefineAsset;

        [Tooltip("iOS 전용: 빌드 뒤 Xcode 프로젝트에 적용할 설정 에셋 (Info.plist · Capabilities · Build Settings 등)")]
        [SerializeField] public HBXcodeSettings XcodeSettingsAsset;

        /// <summary> HB_Build가 BuildContext.CustomData에 쓰는 키 (CustomEntries에 쓰면 실패) </summary>
        public static readonly IReadOnlyList<string> RESERVED_CUSTOM_KEYS = Array.AsReadOnly(new[]
        {
            "SceneDefine", "ProjectDefine", "ProvisionUUID", "ProvisionType",
            BuildStep_XcodeSettings.CUSTOM_DATA_KEY, BuildStep_XcodeSettings.SNAPSHOT_KEY,
        });

        [Serializable]
        public struct CustomStringEntry
        {
            public string Key;
            public string Value;
        }

        [Header("=== 추가 커스텀 데이터 ===")]
        [Tooltip("커스텀 스텝(IPreBuildStep/IPostBuildStep)에 전달할 확장 Key-Value. HB_Build가 쓰는 키는 쓸 수 없음")]
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

            // 같은 키면 HB_Build 값을 조용히 덮어쓰므로 막는다
            var reserved = CustomEntries?.Where(e => RESERVED_CUSTOM_KEYS.Contains(e.Key?.Trim())).Select(e => e.Key.Trim()).Distinct().ToList();
            if (reserved != null && reserved.Count > 0)
                throw BuildHelper.ThrowError($"Build profile '{name}' CustomEntries use key(s) reserved by HB_Build: {string.Join(", ", reserved)}. Rename them.");

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
                AutoConnectProfiler = AutoConnectProfiler,
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

            // 깨진 참조(Missing)도 넘겨서 스텝이 실패시키게 한다 (== null로 거르면 조용히 빠짐)
            if (!ReferenceEquals(SceneDefineAsset, null))
                context.SetCustomData("SceneDefine", SceneDefineAsset);

            if (!ReferenceEquals(ProjectDefineAsset, null))
                context.SetCustomData("ProjectDefine", ProjectDefineAsset);

            if (!ReferenceEquals(XcodeSettingsAsset, null))
                context.SetCustomData(BuildStep_XcodeSettings.CUSTOM_DATA_KEY, XcodeSettingsAsset);

            if (TargetPlatform == ProjectBuild.ESelfBuildPlatform.iOS)
            {
                bool hasUuid = !string.IsNullOrWhiteSpace(ProvisionUUID);
                if (hasUuid)
                    context.SetCustomData("ProvisionUUID", ProvisionUUID.Trim());
                // 서명 종류는 수동 서명(UUID)이나 AutoSign일 때만 넘긴다. 아니면 프로젝트의 서명 설정을 그대로 쓴다
                if (hasUuid || ProvisionType == EProvisionType.AutoSign)
                    context.SetCustomData("ProvisionType", ProvisionType.ToString());
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
        /// Assets/Editor/HBBuild/Profiles 경로에 BuildProfile_&lt;플랫폼&gt;_&lt;타입&gt; 프로필 에셋을 생성합니다.
        /// 선택 설정 에셋 슬롯은 이때 한 번만 채운다 (기본 경로, 없으면 프로젝트에 하나뿐일 때). 빌드는 슬롯만 본다.
        /// </summary>
        public static HBBuildProfile CreateNewProfileAsset(ProjectBuild.ESelfBuildPlatform platform, EBuildType buildType)
        {
            string folder = BuildHelper.USER_PROFILES_DIR;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/BuildProfile_{platform}_{buildType}.asset");

            var profile = CreateInstance<HBBuildProfile>();
            profile.TargetPlatform = platform;
            profile.BuildType = buildType;
            profile.Version = Application.version;
            profile.Scenes = BuildHelper.GetDefaultBuildScenes().ToList();
            profile.SceneDefineAsset = SceneDefine.LoadIfExists();
            profile.ProjectDefineAsset = ProjectDefine.LoadIfExists();
            // 플랫폼과 관계없이 채운다 (나중에 iOS로 바꿔도 슬롯이 비지 않게. Xcode 스텝은 iOS에서만 돈다)
            profile.XcodeSettingsAsset = HBXcodeSettings.LoadIfExists();

            AssetDatabase.CreateAsset(profile, assetPath);
            // 새 프로필만 저장한다 (SaveAssets는 창에서 편집 중인 다른 프로필의 저장 안 한 값까지 디스크에 쓴다)
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.Refresh();

            Debug.Log($"[HBBuildProfile] Created new build profile at: {assetPath}");
            return profile;
        }
    }
}
