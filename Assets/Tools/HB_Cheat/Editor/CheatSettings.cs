using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HB.Cheat.Editor
{
    public enum CheatInitModeSetting
    {
        AutoOnGameStart = 0,
        Manual = 1
    }

    /// <summary>
    /// HB_Cheat 설정. Editor 폴더에 두므로 빌드에 포함되지 않는다.
    /// 런타임 옵션은 생성 레지스트리 코드로 들어간다 (Resources 에셋을 쓰지 않음).
    /// </summary>
    public class CheatSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Editor/HBCheat/HBCheatSettings.asset";
        public const string DefaultUserDefine = "DEV_CHEAT";

        [Header("Define")]
        [Tooltip("프로젝트 치트 코드용 심볼. 패키지는 HB_CHEAT를 쓰며 둘은 항상 함께 켜고 끈다.")]
        [SerializeField] private string userDefine = DefaultUserDefine;

        [Header("Code")]
        [Tooltip("치트 파일을 만들 기본 폴더")]
        [SerializeField] private string cheatFolder = "Assets/Cheats";
        [Tooltip("Assembly-CSharp에 있는 치트의 레지스트리 생성 폴더 (asmdef에 있는 치트는 해당 asmdef 폴더/Generated)")]
        [SerializeField] private string assemblyCSharpOutputFolder = "Assets/Cheats/Generated";
        [Tooltip("치트 asmdef 이름 (asmdef 방식으로 만들 때)")]
        [SerializeField] private string cheatAsmdefName = "Game.Cheats";
        [Tooltip("치트 asmdef가 참조할 게임 asmdef 이름")]
        [SerializeField] private List<string> cheatAsmdefReferences = new List<string>();
        [Tooltip("컴파일 후 레지스트리를 자동으로 다시 생성")]
        [SerializeField] private bool autoGenerate = true;

        [Header("Runtime")]
        [SerializeField] private CheatInitModeSetting initMode = CheatInitModeSetting.AutoOnGameStart;
        [SerializeField] private bool preferConsoleTab = true;
        [SerializeField] private bool showFloatingButton = true;
        [Tooltip("PC 열기/닫기 키 (Input System Key 또는 KeyCode 이름). 비우면 사용 안 함.")]
        [SerializeField] private string toggleKey = "F8";
        [SerializeField] private float serverTimeoutSeconds = 10f;
        [SerializeField] private int historyCapacity = 50;
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("Release Guard")]
        [Tooltip("Development Build가 아닌 빌드를 릴리즈로 본다 (치트 심볼이 켜져 있으면 빌드 실패)")]
        [SerializeField] private bool treatNonDevelopmentAsRelease = true;
        [Tooltip("이 심볼 중 하나라도 켜져 있으면 릴리즈로 본다 (예: LIVE, RELEASE)")]
        [SerializeField] private List<string> releaseDefines = new List<string> { "LIVE" };
        [Tooltip("릴리즈 빌드에서 컴파일된 스크립트 어셈블리를 하나도 검사하지 못하면 빌드를 실패시킨다")]
        [SerializeField] private bool failWhenUnverifiable = true;

        public string UserDefine => string.IsNullOrWhiteSpace(userDefine) ? DefaultUserDefine : userDefine.Trim();
        public string CheatFolder => Normalize(cheatFolder, "Assets/Cheats");
        public string AssemblyCSharpOutputFolder => Normalize(assemblyCSharpOutputFolder, "Assets/Cheats/Generated");
        public string CheatAsmdefName => string.IsNullOrWhiteSpace(cheatAsmdefName) ? "Game.Cheats" : cheatAsmdefName.Trim();
        public IReadOnlyList<string> CheatAsmdefReferences => cheatAsmdefReferences;
        public bool AutoGenerate => autoGenerate;

        public CheatInitModeSetting InitMode => initMode;
        public bool PreferConsoleTab => preferConsoleTab;
        public bool ShowFloatingButton => showFloatingButton;
        public string ToggleKey => toggleKey ?? string.Empty;
        public float ServerTimeoutSeconds => Mathf.Max(0.5f, serverTimeoutSeconds);
        public int HistoryCapacity => Mathf.Max(1, historyCapacity);
        public bool DontDestroyOnLoad => dontDestroyOnLoad;

        public bool TreatNonDevelopmentAsRelease => treatNonDevelopmentAsRelease;
        public IReadOnlyList<string> ReleaseDefines => releaseDefines;
        public bool FailWhenUnverifiable => failWhenUnverifiable;

        private static CheatSettings s_Cached;

        /// <summary>설정 에셋을 찾는다. 없으면 기본값 인스턴스 (저장하지 않음).</summary>
        public static CheatSettings Load()
        {
            if (s_Cached != null) return s_Cached;

            var asset = AssetDatabase.LoadAssetAtPath<CheatSettings>(DefaultAssetPath);
            if (asset == null)
            {
                var guids = AssetDatabase.FindAssets("t:" + nameof(CheatSettings));
                if (guids.Length > 0)
                    asset = AssetDatabase.LoadAssetAtPath<CheatSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (asset == null)
            {
                asset = CreateInstance<CheatSettings>();
                asset.hideFlags = HideFlags.DontSave;
                return asset;
            }

            s_Cached = asset;
            return asset;
        }

        public static bool AssetExists => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Load()));

        public static CheatSettings CreateAsset()
        {
            var existing = Load();
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(existing))) return existing;

            Directory.CreateDirectory(Path.GetDirectoryName(DefaultAssetPath));
            var asset = CreateInstance<CheatSettings>();
            AssetDatabase.CreateAsset(asset, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            s_Cached = asset;
            return asset;
        }

        private static string Normalize(string path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
