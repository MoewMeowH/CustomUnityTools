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

        /// <summary>
        /// 패키지에 포함된 기본 설정 (사용자 설정이 없을 때 사용)
        /// </summary>
        private const string DefaultResourcePath = "InGameConsoleSettings";

        private static InGameConsoleSettings s_Instance;

#if !(UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER)
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnDomainReload()
        {
            s_Instance = null;
        }
#endif
#endif

        [Header("Initialization")]
        [SerializeField] private ConsoleInitMode initMode = ConsoleInitMode.AutoOnGameStart;
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("UI")]
        [SerializeField] private bool autoCreateUI = true;
        [SerializeField] private bool showFpsGraph = true;

        [Header("Resources")]
        [SerializeField] private VisualTreeAsset consoleVisualTree;
        [SerializeField] private PanelSettings panelSettings;

        public ConsoleInitMode InitMode
        {
            get => initMode;
            set => initMode = value;
        }

        public bool DontDestroyOnLoad
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
                    // 사용자 설정 > 패키지 기본 설정 순
                    s_Instance = Resources.Load<InGameConsoleSettings>(UserResourcePath);
                    if (s_Instance == null)
                        s_Instance = Resources.Load<InGameConsoleSettings>(DefaultResourcePath);
#if UNITY_EDITOR
                    if (s_Instance == null)
                    {
                        string[] sGuids = UnityEditor.AssetDatabase.FindAssets("t:InGameConsoleSettings");
                        if (sGuids != null && sGuids.Length > 0)
                        {
                            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(sGuids[0]);
                            s_Instance = UnityEditor.AssetDatabase.LoadAssetAtPath<InGameConsoleSettings>(path);
                        }
                    }
#endif
                    if (s_Instance == null)
                    {
                        s_Instance = CreateInstance<InGameConsoleSettings>();
                        s_Instance.name = "InGameConsoleSettings";
                        s_Instance.consoleVisualTree = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
                        s_Instance.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");

#if UNITY_EDITOR
                        if (s_Instance.consoleVisualTree == null)
                        {
                            string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
                            if (vGuids != null && vGuids.Length > 0)
                            {
                                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                                s_Instance.consoleVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                            }
                        }

                        if (s_Instance.panelSettings == null)
                        {
                            string[] pGuids = UnityEditor.AssetDatabase.FindAssets("HB_PanelSettings t:PanelSettings");
                            if (pGuids != null && pGuids.Length > 0)
                            {
                                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(pGuids[0]);
                                s_Instance.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                            }
                        }
#endif
                    }
                }
                return s_Instance;
            }
        }

        /// <summary>
        /// 캐시된 설정을 버리고 다음 접근 시 다시 로드합니다. (사용자 설정 에셋을 새로 만든 뒤 호출)
        /// </summary>
        public static void ReloadInstance()
        {
            s_Instance = null;
        }
    }
}
