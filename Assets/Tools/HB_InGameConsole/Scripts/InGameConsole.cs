#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER
[AutoStaticsCleanup]
#endif
public static partial class InGameConsole
{
    private static HB_InGameConsoleUI s_UIInstance;
    private static bool s_IsInitialized = false;

    public static bool IsInitialized => s_IsInitialized;

    public static HB_InGameConsoleUI Instance
    {
        get
        {
            if (s_UIInstance == null)
            {
#if UNITY_2023_1_OR_NEWER
                s_UIInstance = Object.FindAnyObjectByType<HB_InGameConsoleUI>();
#else
                s_UIInstance = Object.FindObjectOfType<HB_InGameConsoleUI>();
#endif
            }
            return s_UIInstance;
        }
    }

#if !(UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_0_OR_NEWER)
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnDomainReload()
    {
        s_UIInstance = null;
        s_IsInitialized = false;
    }
#endif
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitializeOnGameStart()
    {
        var settings = InGameConsoleSettings.Instance;
        if (settings != null && settings.InitMode == ConsoleInitMode.AutoOnGameStart)
        {
            Initialize();
        }
    }

    public static void Initialize()
    {
        if (s_IsInitialized) return;

        HLogCore.Initialize();

        var settings = InGameConsoleSettings.Instance;
        if (settings != null && settings.AutoCreateUI)
        {
            EnsureUIInstance();
        }

        s_IsInitialized = true;
    }

    public static HB_InGameConsoleUI EnsureUIInstance()
    {
        if (Instance != null)
        {
            return s_UIInstance;
        }

        var settings = InGameConsoleSettings.Instance;
        var go = new GameObject("[InGameConsole]");
        
        var uiDocument = go.AddComponent<UIDocument>();
        if (settings != null)
        {
            uiDocument.panelSettings = settings.PanelSettings != null 
                ? settings.PanelSettings 
                : Resources.Load<PanelSettings>("HB_PanelSettings");

            uiDocument.visualTreeAsset = settings.ConsoleVisualTree != null 
                ? settings.ConsoleVisualTree 
                : Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

        if (uiDocument.panelSettings == null)
        {
            uiDocument.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");
        }
        if (uiDocument.visualTreeAsset == null)
        {
            uiDocument.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

#if UNITY_EDITOR
        if (uiDocument.panelSettings == null)
        {
            string[] pGuids = UnityEditor.AssetDatabase.FindAssets("HB_PanelSettings t:PanelSettings");
            if (pGuids != null && pGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(pGuids[0]);
                uiDocument.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            }
        }

        if (uiDocument.visualTreeAsset == null)
        {
            string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
            if (vGuids != null && vGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                uiDocument.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            }
        }
#endif

        if (uiDocument.panelSettings == null)
        {
            var fallback = ScriptableObject.CreateInstance<PanelSettings>();
            fallback.name = "HB_RuntimePanelSettings";
            fallback.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            fallback.referenceResolution = new Vector2Int(1200, 800);
            fallback.match = 0.5f;
            fallback.sortingOrder = 1000;
            uiDocument.panelSettings = fallback;
        }

        s_UIInstance = go.AddComponent<HB_InGameConsoleUI>();

        bool shouldDontDestroy = settings == null || settings.DontDestroyOnLoad;
        if (shouldDontDestroy)
        {
            Object.DontDestroyOnLoad(go);
        }

        return s_UIInstance;
    }

    public static void Open() => Instance?.OpenConsole();
    public static void Close() => Instance?.CloseConsole();
    public static void Toggle() => Instance?.ToggleConsole();
    public static void ClearLogs() => HLogCore.Clear();
}
