using UnityEngine;
using UnityEngine.UIElements;

namespace InGameConsoleSystem
{
    public enum ConsoleInitMode
    {
        AutoOnGameStart = 0,
        Manual = 1
    }

    public class InGameConsoleSettings : ScriptableObject
    {
        private const string ResourcePath = "InGameConsoleSettings";
        private static InGameConsoleSettings s_Instance;

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
                    s_Instance = Resources.Load<InGameConsoleSettings>(ResourcePath);
                    if (s_Instance == null)
                    {
                        s_Instance = CreateInstance<InGameConsoleSettings>();
                        s_Instance.name = "InGameConsoleSettings";
                        s_Instance.consoleVisualTree = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
                        s_Instance.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");
                    }
                }
                return s_Instance;
            }
        }
    }
}
