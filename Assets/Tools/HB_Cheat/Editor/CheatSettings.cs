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
        [Tooltip("프로젝트 치트 코드용 심볼. 패키지는 HB_CHEAT를 쓰며 둘은 항상 함께 켜고 끈다. 영문자 · 숫자 · '_'만 (숫자로 시작 불가), 아니면 DEV_CHEAT를 쓴다.")]
        [Delayed] [SerializeField] private string userDefine = DefaultUserDefine;

        [Header("Code")]
        [Tooltip("치트 파일을 만들 기본 폴더")]
        [Delayed] [SerializeField] private string cheatFolder = "Assets/Cheats";
        [Tooltip("Assembly-CSharp에 있는 치트의 레지스트리 생성 폴더 (asmdef에 있는 치트는 해당 asmdef 폴더/Generated)")]
        [Delayed] [SerializeField] private string assemblyCSharpOutputFolder = "Assets/Cheats/Generated";
        [Tooltip("치트 asmdef 이름 (asmdef 방식으로 만들 때)")]
        [Delayed] [SerializeField] private string cheatAsmdefName = "Game.Cheats";
        [Tooltip("치트 asmdef가 참조할 게임 asmdef 이름")]
        [SerializeField] private List<string> cheatAsmdefReferences = new List<string>();
        [Tooltip("컴파일 후 레지스트리를 자동으로 다시 생성")]
        [SerializeField] private bool autoGenerate = true;

        [Header("Runtime")]
        [SerializeField] private CheatInitModeSetting initMode = CheatInitModeSetting.AutoOnGameStart;
        [SerializeField] private bool preferConsoleTab = true;
        [SerializeField] private bool showFloatingButton = true;
        [Tooltip("PC 열기/닫기 키 (Input System Key 또는 KeyCode 이름). 비우면 사용 안 함.")]
        [Delayed] [SerializeField] private string toggleKey = "F8";
        [Delayed] [SerializeField] private float serverTimeoutSeconds = 10f;
        [Delayed] [SerializeField] private int historyCapacity = 50;
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("Automation")]
        [Tooltip("Unity CLI cheat_raw / 브리지 raw로 서버에 임의 문자열을 보내도록 허용 (기본 꺼짐). 패널 Raw 탭은 항상 허용.")]
        [SerializeField] private bool allowRawFromAutomation;
        [Tooltip("Unity CLI cheat_play로 시작한 Play 동안만 Application.runInBackground를 켜고 끝나면 되돌린다 (창이 뒤에 있어도 프레임 진행). 디스크 설정은 쓰지 않고, 원래 true면 아무것도 하지 않는다. editor_play와 사람이 누른 Play에는 영향 없음.")]
        [SerializeField] private bool runInBackgroundDuringCliPlay = true;

        [Header("Release Guard")]
        [Tooltip("Development Build가 아닌 빌드를 릴리즈로 본다 (치트 심볼이 켜져 있으면 빌드 실패)")]
        [SerializeField] private bool treatNonDevelopmentAsRelease = true;
        [Tooltip("이 심볼 중 하나라도 켜져 있으면 릴리즈로 본다 (예: LIVE, RELEASE)")]
        [SerializeField] private List<string> releaseDefines = new List<string> { "LIVE" };
        [Tooltip("릴리즈 빌드에서 컴파일된 스크립트 어셈블리를 하나도 검사하지 못하면 빌드를 실패시킨다")]
        [SerializeField] private bool failWhenUnverifiable = true;

        public string UserDefine
        {
            get
            {
                if (string.IsNullOrWhiteSpace(userDefine)) return DefaultUserDefine;
                string trimmed = userDefine.Trim();
                if (IsValidDefine(trimmed)) return trimmed;

                // 생성 코드 #if가 깨지거나(공백 · 하이픈) PlayerSettings에서 둘로 쪼개지는(;) 이름 → 기본값
                if (s_WarnedInvalidDefine != trimmed)
                {
                    s_WarnedInvalidDefine = trimmed;
                    Debug.LogWarning($"[HB_Cheat] Project define '{trimmed}' is not a valid symbol name (letters, digits, '_', not starting with a digit). Using '{DefaultUserDefine}'.");
                }
                return DefaultUserDefine;
            }
        }

        private static string s_WarnedInvalidDefine;

        /// <summary>전처리기 심볼로 쓸 수 있는 이름인지 (ASCII 영문자 · 숫자 · '_', 숫자로 시작하지 않음)</summary>
        internal static bool IsValidDefine(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
                bool digit = c >= '0' && c <= '9';
                if (!(letter || (digit && i > 0))) return false;
            }
            return true;
        }
        public string CheatFolder => Normalize(cheatFolder, "Assets/Cheats");
        public string AssemblyCSharpOutputFolder => Normalize(assemblyCSharpOutputFolder, "Assets/Cheats/Generated");
        public string CheatAsmdefName => string.IsNullOrWhiteSpace(cheatAsmdefName) ? "Game.Cheats" : cheatAsmdefName.Trim();
        public IReadOnlyList<string> CheatAsmdefReferences => cheatAsmdefReferences;
        public bool AutoGenerate => autoGenerate;

        public CheatInitModeSetting InitMode => initMode;
        public bool PreferConsoleTab => preferConsoleTab;
        public bool ShowFloatingButton => showFloatingButton;
        public string ToggleKey => toggleKey ?? string.Empty;
        // NaN · 무한대는 Mathf.Max를 통과하고 생성 코드 리터럴로도 쓸 수 없다 → 기본값
        public float ServerTimeoutSeconds => float.IsNaN(serverTimeoutSeconds) || float.IsInfinity(serverTimeoutSeconds) ? 10f : Mathf.Max(0.5f, serverTimeoutSeconds);
        public int HistoryCapacity => Mathf.Max(1, historyCapacity);
        public bool DontDestroyOnLoad => dontDestroyOnLoad;
        public bool AllowRawFromAutomation => allowRawFromAutomation;
        public bool RunInBackgroundDuringCliPlay => runInBackgroundDuringCliPlay;

        public bool TreatNonDevelopmentAsRelease => treatNonDevelopmentAsRelease;
        public IReadOnlyList<string> ReleaseDefines => releaseDefines;
        public bool FailWhenUnverifiable => failWhenUnverifiable;

        private static CheatSettings s_Cached;
        // 에셋이 없을 때 쓰는 기본값 인스턴스. 호출마다 만들면 DontSave 객체가 쌓인다
        private static CheatSettings s_Default;

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
                if (s_Default == null)
                    s_Default = FindLeftoverDefault() ?? CreateDefault();
                return s_Default;
            }

            s_Cached = asset;
            ReleaseDefault();
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
            ReleaseDefault();
            return asset;
        }

        private static CheatSettings CreateDefault()
        {
            var instance = CreateInstance<CheatSettings>();
            instance.hideFlags = HideFlags.DontSave;
            return instance;
        }

        // DontSave 객체는 도메인 리로드를 넘겨 남고 정적 참조만 사라진다 → 리로드 뒤에는 남은 것을 다시 쓴다
        private static CheatSettings FindLeftoverDefault()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<CheatSettings>())
            {
                if ((candidate.hideFlags & HideFlags.DontSave) != 0 && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(candidate)))
                    return candidate;
            }
            return null;
        }

        private static void ReleaseDefault()
        {
            if (s_Default == null) s_Default = FindLeftoverDefault();
            if (s_Default == null) return;
            DestroyImmediate(s_Default);
            s_Default = null;
        }

        /// <summary>생성 레지스트리 코드에 들어가는 값 (바뀌었는지 비교용)</summary>
        internal string RegistryInputs => string.Join("|",
            UserDefine, AssemblyCSharpOutputFolder, AutoGenerate, InitMode, PreferConsoleTab, ShowFloatingButton,
            ToggleKey, ServerTimeoutSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            HistoryCapacity, DontDestroyOnLoad, AllowRawFromAutomation);

        private static string Normalize(string path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
