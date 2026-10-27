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
        public const string BUILD_TARGET = "buildTarget";
        public const string BUILD_TYPE = "buildType";
        public const string BUILD_VERSION = "buildVersion";
        public const string VERSION_CODE = "versionCode";
        public const string IS_AAB = "isAab";
        public const string SCENE_LIST = "sceneList";
        public const string DEV_BUILD = "devBuild";
        public const string AUTO_PROF = "autoProfile";
        public const string DEEP_PROF = "deepProfile";
        public const string CLEAN_BUILD = "cleanBuild";
        public const string OUTPUT_PATH = "outputPath";
        public const string ZIP_PACKAGE = "zipPackage";
        public const string BUILD_PROFILE = "buildProfile";
        public const string DISABLED_STEPS = "disabledSteps";

        public const string USER_BUILD_DATA_DIR = "Assets/Editor/HBBuild";
        public const string USER_PROFILES_DIR = "Assets/Editor/HBBuild/Profiles";
        public const string USER_DEFINE_CONTAINER_PATH = "Assets/Editor/HBBuild/DefineContainer.asset";
        public const string USER_SCENE_DEFINE_PATH = "Assets/Editor/HBBuild/SceneDefine.asset";
        public const string USER_XCODE_SETTINGS_PATH = "Assets/Editor/HBBuild/XcodeSettings.asset";
        public const string NONE_SCENE_LIST = "NONE";

        private static List<string> s_Args = new List<string>();
        private const string LOG_HEAD = "[HB_Build]";

        public static bool IsAutoProfile = false;
        public static string ProjectPath = string.Empty;

    #if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ResetStaticsOnDomainReload()
        {
            s_Args = new List<string>();
            IsAutoProfile = false;
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

        public static void ReadDevBuildState()
        {
            // 잘못된 값은 조용히 무시하지 않고 빌드 시작 전에 실패시킨다 (TryGetBoolArg가 예외를 던짐)
            // devBuild · deepProfile · cleanBuild 값 자체는 BuildContext를 만들 때 다시 읽는다
            TryGetBoolArg(DEV_BUILD, out _);
            TryGetBoolArg(DEEP_PROF, out _);
            TryGetBoolArg(CLEAN_BUILD, out _);
            if (TryGetBoolArg(AUTO_PROF, out bool autoProf)) IsAutoProfile = autoProf;
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
        /// 기본 출력 경로 (프로젝트 루트 기준 상대 경로). 절대 경로를 프로필에 저장하지 않도록 상대 경로로 반환합니다.
        /// </summary>
        public static string GetDefaultOutputPath(BuildTarget target)
        {
            return Path.Combine("BuildOutput", target.ToString()).Replace('\\', '/');
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
        /// 빌드에 쓸 설정 에셋(DefineContainer · SceneDefine)을 찾습니다.
        /// 기본 경로에 파일이 있으면 그것을 쓰고(읽지 못하면 실패), 없으면 프로젝트 전체에서 찾습니다.
        /// 여러 개면 어느 것이 쓰일지 사람이 알 수 없으므로 LIVE는 실패, DEV · QA는 경로 순 첫 번째를 쓰고 경고합니다.
        /// </summary>
        internal static T LoadSettingsAssetForBuild<T>(string defaultPath, BuildContext context) where T : UnityEngine.Object
        {
            string typeName = typeof(T).Name;
            if (File.Exists(defaultPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(defaultPath);
                if (asset == null)
                {
                    // 파일은 있는데 아직 임포트 전일 수 있다 (자동 Refresh 꺼짐, git pull 직후 등)
                    AssetDatabase.ImportAsset(defaultPath);
                    asset = AssetDatabase.LoadAssetAtPath<T>(defaultPath);
                }
                if (asset == null)
                    throw ThrowError($"{defaultPath} exists but cannot be loaded as {typeName} (different type, import error, or missing script).");
                return asset;
            }

            var paths = FindAssetPaths<T>();
            if (paths.Count == 0) return null;
            if (paths.Count > 1)
            {
                string list = string.Join(", ", paths);
                if (context.BuildType == EBuildType.LIVE)
                    throw ThrowError($"Multiple {typeName} assets found and none at {defaultPath}: {list}. Move the one to use to {defaultPath} or set it in the build profile.");
                context.AddWarning($"Multiple {typeName} assets found and none at {defaultPath}: {list}. Using '{paths[0]}' (LIVE builds fail on this).");
            }
            return AssetDatabase.LoadAssetAtPath<T>(paths[0]);
        }

        /// <summary>
        /// 빌드 밖(창의 자동 연결 등)에서 설정 에셋 찾기: 기본 경로, 없으면 프로젝트에 하나뿐일 때 그것.
        /// 여러 개면 null (임의로 고르지 않는다 — 빌드 때 LoadSettingsAssetForBuild가 LIVE 실패 · DEV · QA 경고로 처리)
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
