#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System.Reflection;
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 인게임 콘솔 진입점. USE_HLOG 심볼이 있을 때만 동작하며(에디터 포함), 없으면 모든 API가 아무것도 하지 않습니다.
/// 공개 API는 메인 스레드 전용입니다 (다른 스레드에서 호출하면 처리하지 않고 처음 한 번만 에러 로그).
/// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
[AutoStaticsCleanup]
#endif
public static partial class InGameConsole
{
#if USE_HLOG
    private static HB_InGameConsoleUI s_UIInstance;
    private static PanelSettings s_DefaultPanelSettings;
    private static bool s_IsInitialized = false;
#endif

    public static bool IsInitialized
    {
#if USE_HLOG
        get => s_IsInitialized;
#else
        get => false;
#endif
    }

    public static HB_InGameConsoleUI Instance
    {
        get
        {
#if USE_HLOG
            if (!HLogCore.RequireMainThread(HLogCore.MainThreadApi.ConsoleUI)) return null;
            if (s_UIInstance == null)
            {
#if UNITY_2023_1_OR_NEWER
                s_UIInstance = Object.FindAnyObjectByType<HB_InGameConsoleUI>();
#else
                s_UIInstance = Object.FindObjectOfType<HB_InGameConsoleUI>();
#endif
            }
            return s_UIInstance;
#else
            return null;
#endif
        }
    }

#if USE_HLOG
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
#endif

    public static void Initialize()
    {
#if USE_HLOG
        if (!HLogCore.RequireMainThread(HLogCore.MainThreadApi.ConsoleUI)) return;
        if (s_IsInitialized) return;

        HLogCore.Initialize();

        var settings = InGameConsoleSettings.Instance;
        if (settings != null && settings.AutoCreateUI)
        {
            EnsureUIInstance();
        }

        s_IsInitialized = true;
#endif
    }

    public static HB_InGameConsoleUI EnsureUIInstance()
    {
#if USE_HLOG
        if (!HLogCore.RequireMainThread(HLogCore.MainThreadApi.ConsoleUI)) return null;
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
#else
        return null;
#endif
    }

#if USE_HLOG
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

        // 테마를 못 찾은 경우에도 경고가 반복되지 않도록 (코드로 만든 기본 객체에만 적용).
        // 비공개 필드라 Unity 버전에 따라 없을 수 있으며, 없으면 경고 억제만 빠진다.
        try
        {
            var field = typeof(PanelSettings).GetField("m_DisableNoThemeWarning", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(panelSettings, true);
        }
        catch (System.Exception)
        {
            // 경고 억제 실패는 동작에 영향이 없다
        }

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
#endif

    public static void Open()
    {
#if USE_HLOG
        var ui = Instance;
        if (ui != null) ui.OpenConsole();
#endif
    }

    public static void Close()
    {
#if USE_HLOG
        var ui = Instance;
        if (ui != null) ui.CloseConsole();
#endif
    }

    public static void Toggle()
    {
#if USE_HLOG
        var ui = Instance;
        if (ui != null) ui.ToggleConsole();
#endif
    }

    public static void ClearLogs() => HLogCore.Clear();

    /// <summary>
    /// 콘솔 상단 탭에 외부 뷰를 붙입니다 (예: HB_Cheat). 콘솔 UI가 없으면 false.
    /// </summary>
    public static bool AddExternalView(string viewName, VisualElement content)
    {
#if USE_HLOG
        var ui = Instance;
        if (ui == null) return false;
        ui.AddExternalView(viewName, content);
        return true;
#else
        return false;
#endif
    }

    public static void RemoveExternalView(string viewName)
    {
#if USE_HLOG
        var ui = Instance;
        if (ui != null) ui.RemoveExternalView(viewName);
#endif
    }

    public static void ShowExternalView(string viewName)
    {
#if USE_HLOG
        var ui = Instance;
        if (ui != null) ui.ShowExternalView(viewName);
#endif
    }

    public static bool IsExternalViewActive(string viewName)
    {
#if USE_HLOG
        var ui = Instance;
        return ui != null && ui.IsExternalViewActive(viewName);
#else
        return false;
#endif
    }
}
