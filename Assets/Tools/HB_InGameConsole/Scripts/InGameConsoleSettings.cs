#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace InGameConsoleSystem
{
    public enum ConsoleInitMode
    {
        AutoOnGameStart = 0,
        Manual = 1
    }

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public class InGameConsoleSettings : ScriptableObject
    {
        /// <summary>
        /// 사용 프로젝트의 설정 (Assets/Resources). 패키지는 UPM으로 설치되면 읽기 전용이라 사용자 설정은 별도 에셋으로 둔다.
        /// </summary>
        public const string UserResourcePath = "HB_InGameConsoleUserSettings";
        public const string UserAssetPath = "Assets/Resources/" + UserResourcePath + ".asset";

        private static InGameConsoleSettings s_Instance;

#if UNITY_EDITOR
        // 모든 Unity 버전에서 플레이 진입마다 다시 읽는다 (도메인 리로드를 꺼도 · 6.5+ NoAutoStaticsCleanup이어도 같은 수명).
        // 에디트 모드에서 설정 에셋을 새로 만들거나 바꾼 것이 다음 플레이에 반영되게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayStart()
        {
            s_Instance = null;
        }
#endif

        [Header("Initialization")]
        [SerializeField] private ConsoleInitMode initMode = ConsoleInitMode.AutoOnGameStart;
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("UI")]
        [SerializeField] private bool autoCreateUI = true;
        [SerializeField] private bool showFpsGraph = true;
        [Tooltip("맨 아래를 보고 있을 때 새 로그를 따라 자동으로 스크롤합니다. 위로 스크롤하면 멈추고, 다시 맨 아래로 내리면 이어서 따라갑니다.")]
        [SerializeField] private bool autoScroll = true;

        [Header("Resources")]
        [SerializeField] private VisualTreeAsset consoleVisualTree;
        [SerializeField] private PanelSettings panelSettings;

        public ConsoleInitMode InitMode
        {
            get => initMode;
            set => initMode = value;
        }

        public bool PersistAcrossScenes
        {
            get => dontDestroyOnLoad;
            set => dontDestroyOnLoad = value;
        }

        public bool AutoCreateUI
        {
            get => autoCreateUI;
            set => autoCreateUI = value;
        }

        public bool ShowFpsGraph
        {
            get => showFpsGraph;
            set => showFpsGraph = value;
        }

        public bool AutoScroll
        {
            get => autoScroll;
            set => autoScroll = value;
        }

        public VisualTreeAsset ConsoleVisualTree
        {
            get => consoleVisualTree;
            set => consoleVisualTree = value;
        }

        public PanelSettings PanelSettings
        {
            get => panelSettings;
            set => panelSettings = value;
        }

        public static InGameConsoleSettings Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    // 사용자 설정이 없으면 코드 기본값 (패키지에 .asset을 두지 않음: 상위 Unity 버전 직렬화 호환 문제).
                    // 에디터도 빌드와 같은 경로(Resources)만 읽는다. 다른 위치의 에셋을 에디터에서만 쓰면 빌드 동작이 달라진다.
                    s_Instance = Resources.Load<InGameConsoleSettings>(UserResourcePath);
#if UNITY_EDITOR
                    if (s_Instance == null)
                    {
                        WarnMisplacedSettingsOnce();
                    }
#endif
                    if (s_Instance == null)
                    {
                        s_Instance = CreateInstance<InGameConsoleSettings>();
                        s_Instance.name = "InGameConsoleSettings";
                        // VisualTree/PanelSettings는 비워 두면 InGameConsole이 패키지 기본값(UXML, 코드 생성 PanelSettings)을 사용
                    }
                }
                return s_Instance;
            }
        }

#if UNITY_EDITOR
        private static bool s_WarnedMisplaced;

        private static void WarnMisplacedSettingsOnce()
        {
            if (s_WarnedMisplaced) return;
            s_WarnedMisplaced = true;

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:InGameConsoleSettings");
            if (guids == null || guids.Length == 0) return;

            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            Debug.LogWarning($"[InGameConsole] 설정 에셋 '{path}'은(는) 빌드에서 읽히지 않는 위치라 사용하지 않습니다. " +
                             $"'{UserAssetPath}'로 옮기세요 (Tools > HB > In-Game Console > Settings).");
        }
#endif

        /// <summary>
        /// 캐시된 설정을 버리고 다음 접근 시 다시 로드합니다. (사용자 설정 에셋을 새로 만든 뒤 호출)
        /// </summary>
        public static void ReloadInstance()
        {
            s_Instance = null;
        }
    }
}
