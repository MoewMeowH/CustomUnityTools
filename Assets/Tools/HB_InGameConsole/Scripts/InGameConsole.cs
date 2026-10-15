#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System.Reflection;
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
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

#if !(UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER)
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnDomainReload()
    {
        s_UIInstance = null;
        s_IsInitialized = false;
    }
#endif
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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

#if UNITY_6000_5_OR_NEWER
        // Unity 6+ 권장 PanelRenderer 컴포넌트 생성 (UIDocument 대체)
        var panelRenderer = go.AddComponent<PanelRenderer>();
        panelRenderer.sortingOrder = 9999;

        if (settings != null)
        {
            panelRenderer.panelSettings = settings.PanelSettings != null 
                ? settings.PanelSettings 
                : Resources.Load<PanelSettings>("HB_PanelSettings");

            panelRenderer.visualTreeAsset = settings.ConsoleVisualTree != null 
                ? settings.ConsoleVisualTree 
                : Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

        if (panelRenderer.panelSettings == null)
        {
            panelRenderer.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");
        }
        if (panelRenderer.visualTreeAsset == null)
        {
            panelRenderer.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

#if UNITY_EDITOR
        if (panelRenderer.panelSettings == null)
        {
            string[] pGuids = UnityEditor.AssetDatabase.FindAssets("HB_PanelSettings t:PanelSettings");
            if (pGuids != null && pGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(pGuids[0]);
                panelRenderer.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            }
        }

        if (panelRenderer.visualTreeAsset == null)
        {
            string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
            if (vGuids != null && vGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                panelRenderer.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            }
        }
#endif

        if (panelRenderer.panelSettings == null)
        {
            var fallback = ScriptableObject.CreateInstance<PanelSettings>();
            fallback.name = "HB_RuntimePanelSettings";
            fallback.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            fallback.referenceResolution = new Vector2Int(1200, 800);
            fallback.match = 0.5f;
            fallback.sortingOrder = 1000;
            panelRenderer.panelSettings = fallback;
        }

        EnsureThemeStyleSheet(panelRenderer.panelSettings);
#else
        // Unity 2021 / 2022 호환: UIDocument 컴포넌트 생성
        var uiDoc = go.AddComponent<UIDocument>();
        uiDoc.sortingOrder = 9999;

        if (settings != null)
        {
            uiDoc.panelSettings = settings.PanelSettings != null 
                ? settings.PanelSettings 
                : Resources.Load<PanelSettings>("HB_PanelSettings");

            uiDoc.visualTreeAsset = settings.ConsoleVisualTree != null 
                ? settings.ConsoleVisualTree 
                : Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

        if (uiDoc.panelSettings == null)
        {
            uiDoc.panelSettings = Resources.Load<PanelSettings>("HB_PanelSettings");
        }
        if (uiDoc.visualTreeAsset == null)
        {
            uiDoc.visualTreeAsset = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
        }

#if UNITY_EDITOR
        if (uiDoc.panelSettings == null)
        {
            string[] pGuids = UnityEditor.AssetDatabase.FindAssets("HB_PanelSettings t:PanelSettings");
            if (pGuids != null && pGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(pGuids[0]);
                uiDoc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            }
        }

        if (uiDoc.visualTreeAsset == null)
        {
            string[] vGuids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
            if (vGuids != null && vGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(vGuids[0]);
                uiDoc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            }
        }
#endif

        if (uiDoc.panelSettings == null)
        {
            var fallback = ScriptableObject.CreateInstance<PanelSettings>();
            fallback.name = "HB_RuntimePanelSettings";
            fallback.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            fallback.referenceResolution = new Vector2Int(1200, 800);
            fallback.match = 0.5f;
            fallback.sortingOrder = 1000;
            uiDoc.panelSettings = fallback;
        }

        EnsureThemeStyleSheet(uiDoc.panelSettings);
#endif

        s_UIInstance = go.AddComponent<HB_InGameConsoleUI>();

        bool shouldDontDestroy = settings == null || settings.DontDestroyOnLoad;
        if (shouldDontDestroy)
        {
            Object.DontDestroyOnLoad(go);
        }

        return s_UIInstance;
    }

    internal static void EnsureThemeStyleSheet(PanelSettings panelSettings)
    {
        if (panelSettings == null) return;

        if (panelSettings.themeStyleSheet == null)
        {
            var theme = Resources.Load<ThemeStyleSheet>("HB_DefaultRuntimeTheme");
            if (theme != null)
            {
                panelSettings.themeStyleSheet = theme;
            }
#if UNITY_EDITOR
            if (panelSettings.themeStyleSheet == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("HB_DefaultRuntimeTheme t:ThemeStyleSheet");
                if (guids != null && guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    panelSettings.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
                }
            }
#endif
        }

        try
        {
            var field = typeof(PanelSettings).GetField("m_DisableNoThemeWarning", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(panelSettings, true);
            }
        }
        catch { }
    }

    public static void Open() => Instance?.OpenConsole();
    public static void Close() => Instance?.CloseConsole();
    public static void Toggle() => Instance?.ToggleConsole();
    public static void ClearLogs() => HLogCore.Clear();
}
