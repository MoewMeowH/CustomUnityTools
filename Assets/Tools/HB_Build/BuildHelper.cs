using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.CustomBuild.Editor
{
    public class JenkinsBuildException : BuildFailedException
    {
        public JenkinsBuildException(string message) : base(message) { }
        public JenkinsBuildException(Exception innerException) : base(innerException) { }
    }

    public enum EBuildType
    {
        DEV,
        QA,
        LIVE,
    }

    public enum EPlatformType
    {
        NONE,
        PC,
        MOBILE,
    }

    #if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
    #endif
    public static class BuildHelper
    {
        public const string BUILD_PROFILE = "buildProfile";
        // 프로필 값을 덮어쓸 수 있는 인자는 이 셋뿐이다 (나머지 빌드 설정은 프로필에 커밋되어야 추적된다)
        public const string BUILD_VERSION = "buildVersion";
        public const string VERSION_CODE = "versionCode";
        public const string DEV_BUILD = "devBuild";

        /// <summary> 예전에 받던 빌드 인자. 넘기면 조용히 무시하지 않고 실패시킨다 (값이 바뀐 줄 알지 않게) </summary>
        internal static readonly string[] REMOVED_ARGS =
        {
            "buildType", "isAab", "zipPackage", "deepProfile", "cleanBuild", "disabledSteps", "sceneList", "outputPath", "autoProfile"
        };

        public const string USER_BUILD_DATA_DIR = "Assets/Editor/HBBuild";
        public const string USER_PROFILES_DIR = "Assets/Editor/HBBuild/Profiles";
        public const string USER_DEFINE_CONTAINER_PATH = "Assets/Editor/HBBuild/DefineContainer.asset";
        public const string USER_SCENE_DEFINE_PATH = "Assets/Editor/HBBuild/SceneDefine.asset";
        public const string USER_XCODE_SETTINGS_PATH = "Assets/Editor/HBBuild/XcodeSettings.asset";
        /// <summary> 빌드 산출물 폴더 (프로젝트 루트 기준). 출력은 항상 이 아래 &lt;Target&gt; 폴더 </summary>
        public const string BUILD_OUTPUT_DIR = "BuildOutput";

        private static List<string> s_Args = new List<string>();
        private const string LOG_HEAD = "[HB_Build]";

        public static string ProjectPath = string.Empty;

    #if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ResetStaticsOnDomainReload()
        {
            s_Args = new List<string>();
            ProjectPath = string.Empty;
        }
    #endif

        internal static bool IsNullOrEmptyCollection<T>(this ICollection<T> collection)
        {
            return collection == null || collection.Count == 0;
        }

        public static void SetUpAllArgs()
        {
            s_Args = Environment.GetCommandLineArgs().ToList();
            Log($"Command Line Args Count: {s_Args.Count}");
        }

        /// <summary>
        /// 배치 빌드 인자를 빌드 시작 전에 검사한다: 없앤 인자가 있거나 -devBuild 값이 틀리면 실패 (값 자체는 덮어쓸 때 다시 읽음).
        /// </summary>
        public static void ValidateArgs()
        {
            var removed = REMOVED_ARGS.Where(HasArg).Select(a => "-" + a).ToList();
            if (removed.Count > 0)
                throw ThrowError($"{string.Join(", ", removed)} is no longer supported. Set it in the build profile " +
                                 $"(only -{BUILD_VERSION}, -{VERSION_CODE}, -{DEV_BUILD} can override the profile).");
            TryGetBoolArg(DEV_BUILD, out _);
        }

        /// <summary> -buildProfile 값. 배치 빌드는 프로필이 필수다 (없거나 비면 실패) </summary>
        public static string GetRequiredProfileArg()
        {
            string profile = GetArg(BUILD_PROFILE);
            if (string.IsNullOrWhiteSpace(profile))
                throw ThrowError($"-{BUILD_PROFILE} <asset path> is required. Every build runs from a build profile asset.");
            return profile;
        }

        public static void ReadEnvironment()
        {
            var projectPath = GetArg("-projectPath");
            if (string.IsNullOrEmpty(projectPath))
            {
                ProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            else
            {
                ProjectPath = Path.GetFullPath(projectPath);
            }

            Log($"Project Root: {ProjectPath}");
        }

        /// <summary>
        /// 명령줄에 해당 옵션(-name)이 있는지 여부. 값이 비어 있어도 true입니다.
        /// </summary>
        public static bool HasArg(string argName)
        {
            if (string.IsNullOrEmpty(argName) || s_Args == null) return false;

            string name = argName.TrimStart('-');
            return s_Args.Any(a => a.StartsWith("-") && a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// true/false 옵션을 읽습니다. 옵션이 없으면 false를 반환하고, 값이 비었거나 true/false가 아니면 예외를 던집니다.
        /// </summary>
        public static bool TryGetBoolArg(string argName, out bool result)
        {
            result = false;
            string value = GetArg(argName);
            if (string.IsNullOrEmpty(value))
            {
                if (HasArg(argName))
                    throw ThrowError($"-{argName} requires a value (true/false)");
                return false;
            }

            if (!bool.TryParse(value, out result))
            {
                throw ThrowError($"Invalid -{argName} argument: '{value}' (expected true/false)");
            }
            return true;
        }

        public static string GetArg(string argName)
        {
            if (string.IsNullOrEmpty(argName) || s_Args == null || s_Args.Count == 0)
                return string.Empty;

            string name = argName.TrimStart('-');
            for (int i = 0; i < s_Args.Count - 1; i++)
            {
                // 이름은 '-'로 시작하는 토큰만 (다른 인자의 값이 우연히 같은 글자여도 이름으로 보지 않음, HasArg와 같은 규칙)
                if (!s_Args[i].StartsWith("-")) continue;
                if (s_Args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    // Unity는 빈 인자("")를 버리므로 값 자리에 다음 인자 이름이 올 수 있다 → 값이 없는 것으로 본다
                    // (예: -buildVersion "" -versionCode "" 가 -buildVersion -versionCode 로 들어옴)
                    string value = s_Args[i + 1];
                    return value.StartsWith("-") ? string.Empty : value.Trim('\"', '\'');
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 산출물 이름 (확장자 없음): {제품명}_v{버전}[_{버전코드}]_{빌드타입}[_DEVBUILD][_{소스}][_Jenkins{번호}].
        /// 버전 코드 · 소스가 비면 그 칸을 뺀다. jenkinsBuildNumber가 null이면 Jenkins 빌드가 아니다.
        /// </summary>
        public static string ComposeArtifactName(string productName, string version, string versionCode, EBuildType buildType,
            bool isLiveDevBuild, string sourceRevision, string jenkinsBuildNumber)
        {
            string name = $"{productName}_v{version}";
            if (!string.IsNullOrEmpty(versionCode))
                name += "_" + versionCode;
            name += "_" + buildType;
            if (isLiveDevBuild)
                name += "_DEVBUILD";
            if (!string.IsNullOrEmpty(sourceRevision))
                name += "_" + sourceRevision;
            if (jenkinsBuildNumber != null)
                name += "_Jenkins" + jenkinsBuildNumber;
            return name;
        }

        /// <summary>
        /// 출력 경로 BuildOutput/&lt;Target&gt; (프로젝트 루트 기준 상대 경로). 로컬 · Jenkins 모두 이 경로에 만든다.
        /// </summary>
        public static string GetOutputPath(BuildTarget target)
        {
            return Path.Combine(BUILD_OUTPUT_DIR, target.ToString()).Replace('\\', '/');
        }

        /// <summary>
        /// 상대 경로를 프로젝트 루트 기준 절대 경로로 변환합니다.
        /// </summary>
        public static string ResolveProjectRelativePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;

            string root = string.IsNullOrEmpty(ProjectPath)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                : ProjectPath;
            return Path.GetFullPath(Path.Combine(root, path));
        }

        public static void Log(string str)
        {
            Console.WriteLine($"{LOG_HEAD} {str}");
            Debug.Log($"{LOG_HEAD} {str}");
        }    

        public static void LogWarning(string str)
        {
            Console.WriteLine($"{LOG_HEAD} WARN {str}");
            Debug.LogWarning($"{LOG_HEAD} {str}");
        }

        /// <summary>
        /// 빌드 실패 예외를 만들어 돌려줍니다. 이름과 달리 직접 던지지 않으므로 호출부에서 <c>throw BuildHelper.ThrowError(...)</c>로 씁니다.
        /// </summary>
        public static JenkinsBuildException ThrowError(string msg) => new JenkinsBuildException($"{LOG_HEAD} :: {msg}");

        /// <summary> HB_Build가 지원하는 빌드 대상 (Windows · Android · iOS). 그 밖의 대상은 빌드 시작 전에 실패한다 </summary>
        public static bool IsSupportedTarget(BuildTarget buildTarget) => buildTarget is
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 or BuildTarget.Android or BuildTarget.iOS;

        public static BuildTargetGroup GetTargetGroup(BuildTarget buildTarget) => buildTarget switch
        {
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => BuildTargetGroup.Standalone,
            BuildTarget.iOS => BuildTargetGroup.iOS,
            BuildTarget.Android => BuildTargetGroup.Android,
            _ => BuildTargetGroup.Standalone,
        };

        /// <summary>
        /// Build Settings에서 활성화된 씬 목록을 반환합니다. 없으면 빈 배열 (빌드 시 실패 처리됨).
        /// </summary>
        public static string[] GetDefaultBuildScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
        }

        /// <summary>
        /// 프로필 슬롯에서 넘어온 설정 에셋 (CustomData[key]). 슬롯이 비었으면 null — 기본 경로 · 프로젝트에서 찾지 않는다.
        /// 깨진 참조(Missing, 에셋 삭제)나 다른 타입이면 조용히 건너뛰지 않고 실패한다.
        /// </summary>
        internal static T GetSettingsAsset<T>(BuildContext context, string key) where T : UnityEngine.Object
        {
            if (!context.CustomData.TryGetValue(key, out object value))
                return null;
            if (value is not T asset)
                throw ThrowError($"CustomData '{key}' is a {value?.GetType().Name ?? "null"}, not an {typeof(T).Name} asset.");
            // Missing 참조는 관리 객체는 있지만 Unity 비교로 null이다
            if (asset == null)
                throw ThrowError($"The build profile slot '{key}' references a {typeof(T).Name} asset that is missing (deleted or broken reference). Assign it again or clear the slot.");
            return asset;
        }

        /// <summary>
        /// 새 프로필의 슬롯을 채울 설정 에셋 찾기: 기본 경로, 없으면 프로젝트에 하나뿐일 때 그것. 여러 개면 null (임의로 고르지 않음).
        /// 빌드는 이 찾기를 쓰지 않고 프로필 슬롯만 본다.
        /// </summary>
        internal static T LoadSettingsAsset<T>(string defaultPath) where T : UnityEngine.Object
        {
            if (File.Exists(defaultPath))
                return AssetDatabase.LoadAssetAtPath<T>(defaultPath);

            var paths = FindAssetPaths<T>();
            return paths.Count == 1 ? AssetDatabase.LoadAssetAtPath<T>(paths[0]) : null;
        }

        private static List<string> FindAssetPaths<T>() where T : UnityEngine.Object
        {
            // "t:"는 짧은 타입 이름으로 찾으므로 이름이 같은 다른 타입(다른 네임스페이스) 에셋은 메인 에셋 타입으로 거른다
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && typeof(T).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(p)))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Assets/Editor/HBBuild 폴더를 확인하고 없으면 자동 생성합니다.
        /// </summary>
        public static void EnsureUserBuildDataDirExists()
        {
            if (!Directory.Exists(USER_BUILD_DATA_DIR))
            {
                Directory.CreateDirectory(USER_BUILD_DATA_DIR);
                AssetDatabase.Refresh();
            }
        }
    }
}
