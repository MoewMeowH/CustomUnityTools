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
    private static PanelSettings s_DefaultPanelSettings;
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
        s_DefaultPanelSettings = null;
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

        // 테마를 먼저 채운 PanelSettings를 연결해야 "No Theme Style Sheet" 경고가 나지 않는다
        var panelSettings = ResolvePanelSettings(settings);
        var visualTree = ResolveVisualTree(settings);

#if UNITY_6000_5_OR_NEWER
        // Unity 6.5+ 권장 PanelRenderer 컴포넌트 생성 (UIDocument 대체)
        var panelRenderer = go.AddComponent<PanelRenderer>();
        panelRenderer.sortingOrder = 9999;
        panelRenderer.panelSettings = panelSettings;
        panelRenderer.visualTreeAsset = visualTree;
#else
        var uiDoc = go.AddComponent<UIDocument>();
        uiDoc.sortingOrder = 9999;
        uiDoc.panelSettings = panelSettings;
        uiDoc.visualTreeAsset = visualTree;
#endif

        s_UIInstance = go.AddComponent<HB_InGameConsoleUI>();

        bool shouldDontDestroy = settings == null || settings.DontDestroyOnLoad;
        if (shouldDontDestroy)
        {
            Object.DontDestroyOnLoad(go);
        }

        return s_UIInstance;
    }

    /// <summary>
    /// 사용할 PanelSettings를 결정합니다. 설정에 지정된 것이 없으면 코드로 만든 기본 PanelSettings를 씁니다.
    /// (패키지에 .asset을 두면 상위 Unity 버전에서 저장된 직렬화 형식을 하위 버전이 읽지 못하므로 코드로 생성)
    /// 반환 전에 테마를 채우므로 UIDocument/PanelRenderer에 바로 연결해도 됩니다.
    /// </summary>
    internal static PanelSettings ResolvePanelSettings(InGameConsoleSettings settings)
    {
        if (settings != null && settings.PanelSettings != null)
        {
            EnsureThemeStyleSheet(settings.PanelSettings);
            return settings.PanelSettings;
        }

        return GetDefaultPanelSettings();
    }

    internal static VisualTreeAsset ResolveVisualTree(InGameConsoleSettings settings)
    {
        if (settings != null && settings.ConsoleVisualTree != null)
            return settings.ConsoleVisualTree;

        var tree = Resources.Load<VisualTreeAsset>("HB_InGameConsole");
#if UNITY_EDITOR
        if (tree == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("HB_InGameConsole t:VisualTreeAsset");
            if (guids != null && guids.Length > 0)
                tree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
        return tree;
    }

    private static PanelSettings GetDefaultPanelSettings()
    {
        if (s_DefaultPanelSettings != null) return s_DefaultPanelSettings;

        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.name = "HB_RuntimePanelSettings";
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1200, 800);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0f;
        panelSettings.sortingOrder = 1000;
        EnsureThemeStyleSheet(panelSettings);

        // 테마를 못 찾은 경우에도 경고가 반복되지 않도록 (코드로 만든 기본 객체에만 적용)
        try
        {
            var field = typeof(PanelSettings).GetField("m_DisableNoThemeWarning", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(panelSettings, true);
        }
        catch { }

        s_DefaultPanelSettings = panelSettings;
        return panelSettings;
    }

    /// <summary>
    /// 테마가 비어 있으면 패키지 기본 테마(HB_DefaultRuntimeTheme.tss)를 채웁니다.
    /// </summary>
    internal static void EnsureThemeStyleSheet(PanelSettings panelSettings)
    {
        if (panelSettings == null || panelSettings.themeStyleSheet != null) return;

        var theme = Resources.Load<ThemeStyleSheet>("HB_DefaultRuntimeTheme");
#if UNITY_EDITOR
        if (theme == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("HB_DefaultRuntimeTheme t:ThemeStyleSheet");
            if (guids != null && guids.Length > 0)
                theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
        if (theme != null)
            panelSettings.themeStyleSheet = theme;
    }

    public static void Open() => Instance?.OpenConsole();
    public static void Close() => Instance?.CloseConsole();
    public static void Toggle() => Instance?.ToggleConsole();
    public static void ClearLogs() => HLogCore.Clear();

    /// <summary>
    /// 콘솔 상단 탭에 외부 뷰를 붙입니다 (예: HB_Cheat). 콘솔 UI가 없으면 false.
    /// </summary>
    public static bool AddExternalView(string viewName, VisualElement content)
    {
        var ui = Instance;
        if (ui == null) return false;
        ui.AddExternalView(viewName, content);
        return true;
    }

    public static void RemoveExternalView(string viewName)
    {
        var ui = Instance;
        if (ui != null) ui.RemoveExternalView(viewName);
    }

    public static void ShowExternalView(string viewName)
    {
        var ui = Instance;
        if (ui != null) ui.ShowExternalView(viewName);
    }

    public static bool IsExternalViewActive(string viewName)
    {
        var ui = Instance;
        return ui != null && ui.IsExternalViewActive(viewName);
    }
}
