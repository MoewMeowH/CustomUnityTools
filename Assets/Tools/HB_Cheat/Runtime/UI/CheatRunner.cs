using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace HB.Cheat.UI
{
    /// <summary>
    /// CheatSystem의 MonoBehaviour 쪽: 서버 대기 시간 처리, PC 단축키, 안전 영역·OS 키보드 대응, 독립 패널 호스팅.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class CheatRunner : MonoBehaviour
    {
        private const int StandaloneSortingOrder = 1001;

        private CheatPanel m_Panel;
        private UIDocument m_Document;
        private StandaloneHost m_Standalone;
        private PanelSettings m_CreatedPanelSettings;

#if HB_CHEAT_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.Key m_ToggleKey;
#elif ENABLE_LEGACY_INPUT_MANAGER
        private KeyCode m_ToggleKey;
#endif
        private bool m_HasToggleKey;

        public CheatPanel Panel
        {
            get
            {
                if (m_Panel == null)
                {
                    m_Panel = new CheatPanel();
                    m_Panel.CloseRequested += CheatSystem.Close;
                }
                return m_Panel;
            }
        }

        private void Awake()
        {
            string key = CheatSystem.Options.ToggleKey;
            if (string.IsNullOrWhiteSpace(key)) return;

#if HB_CHEAT_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            m_HasToggleKey = Enum.TryParse(key.Trim(), true, out m_ToggleKey);
#elif ENABLE_LEGACY_INPUT_MANAGER
            m_HasToggleKey = Enum.TryParse(key.Trim(), true, out m_ToggleKey);
#endif
            if (!m_HasToggleKey)
                Debug.LogWarning($"[HB_Cheat] Unknown toggle key '{key}'.");
        }

        /// <summary>콘솔에 붙지 못했을 때 자체 UIDocument로 띄운다.</summary>
        public ICheatPanelHost CreateStandaloneHost()
        {
            var settings = CheatSystem.Options.PanelSettings;
            bool usesFallback = settings == null;
            if (usesFallback)
            {
                m_CreatedPanelSettings = CreateDefaultPanelSettings();
                settings = m_CreatedPanelSettings;
            }

            // 비활성 상태에서 설정을 채운 뒤 켜야 rootVisualElement가 올바른 패널로 만들어진다
            var uiObject = new GameObject("UI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(transform, false);
            m_Document = uiObject.AddComponent<UIDocument>();
            m_Document.panelSettings = settings;
            m_Document.sortingOrder = StandaloneSortingOrder;
            uiObject.SetActive(true);

            if (usesFallback || settings.themeStyleSheet == null)
                Panel.ApplyFallbackFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

            m_Standalone = new StandaloneHost(Panel, CheatSystem.Options.ShowFloatingButton);
            EnsureStandaloneAttached();
            return m_Standalone;
        }

        private void Update()
        {
            CheatSystem.Tick();

            if (m_HasToggleKey && ToggleKeyPressed())
                CheatSystem.Toggle();

            if (m_Standalone != null) EnsureStandaloneAttached();
            UpdateInsets();
        }

        private bool ToggleKeyPressed()
        {
#if HB_CHEAT_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard[m_ToggleKey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(m_ToggleKey);
#else
            return false;
#endif
        }

        /// <summary>UIDocument가 루트를 다시 만들면(패널 재로드 등) 다시 붙인다.</summary>
        private void EnsureStandaloneAttached()
        {
            var root = m_Document != null ? m_Document.rootVisualElement : null;
            if (root != null && m_Standalone.Root != root)
                m_Standalone.AttachTo(root);
        }

        /// <summary>안전 영역과 OS 키보드 높이를 패널 좌표로 바꿔 반영한다.</summary>
        private void UpdateInsets()
        {
            if (m_Panel == null || m_Panel.panel == null || Screen.height <= 0) return;

            var panelRoot = m_Panel.panel.visualTree;
            float panelHeight = panelRoot.layout.height;
            if (float.IsNaN(panelHeight) || panelHeight <= 0) return;
            float scale = panelHeight / Screen.height;

            var safe = Screen.safeArea;
            float safeBottom = safe.yMin * scale;

            float keyboard = 0f;
            if (TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible)
                keyboard = TouchScreenKeyboard.area.height * scale;

            if (m_Standalone != null)
            {
                m_Standalone.SetSafeArea(safe.xMin * scale, (Screen.height - safe.yMax) * scale, (Screen.width - safe.xMax) * scale, safeBottom);
                // 창이 이미 안전 영역만큼 올라와 있으므로 키보드는 그만큼 빼고 반영
                m_Panel.SetBottomInsets(0f, Mathf.Max(0f, keyboard - safeBottom - StandaloneHost.WindowMargin));
            }
            else
            {
                m_Panel.SetBottomInsets(0f, keyboard);
            }
        }

        private void OnDestroy()
        {
            CheatSystem.Shutdown();
            if (m_CreatedPanelSettings != null) Destroy(m_CreatedPanelSettings);
        }

        private static PanelSettings CreateDefaultPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HB_CheatPanelSettings";
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = StandaloneSortingOrder;

            // 테마 에셋을 쓰지 않으므로 경고만 끈다 (스타일은 모두 인라인)
            try
            {
                var field = typeof(PanelSettings).GetField("m_DisableNoThemeWarning", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(settings, true);
            }
            catch (Exception)
            {
                // 내부 필드 이름이 바뀌어도 경고만 날 뿐 동작에는 문제 없다
            }
            return settings;
        }
    }

    /// <summary>독립 패널: 전체 화면 창 + (선택) 끌어서 옮길 수 있는 열기 버튼</summary>
    internal sealed class StandaloneHost : ICheatPanelHost
    {
        public const float WindowMargin = 8f;
        private const float WideWindowMinWidth = 1100f;
        private const float DragThreshold = 12f;

        private readonly CheatPanel m_Panel;
        private readonly VisualElement m_Overlay;
        private readonly VisualElement m_Window;
        private readonly Label m_FloatingButton;
        private bool m_IsOpen;
        private float m_SafeLeft, m_SafeTop, m_SafeRight, m_SafeBottom;

        private bool m_Dragging;
        private bool m_PointerDown;
        private Vector2 m_PointerStart;
        private Vector2 m_ButtonStart;
        private bool m_ButtonPlaced;

        public VisualElement Root { get; private set; }
        public bool IsOpen => m_IsOpen;

        public StandaloneHost(CheatPanel panel, bool showFloatingButton)
        {
            m_Panel = panel;

            m_Overlay = new VisualElement { name = "HBCheatOverlay", pickingMode = PickingMode.Ignore };
            m_Overlay.style.position = Position.Absolute;
            m_Overlay.style.left = 0;
            m_Overlay.style.top = 0;
            m_Overlay.style.right = 0;
            m_Overlay.style.bottom = 0;
            m_Overlay.RegisterCallback<GeometryChangedEvent>(_ => Relayout());

            m_Window = new VisualElement { name = "HBCheatWindow" };
            m_Window.style.position = Position.Absolute;
            CheatStyles.Radius(m_Window.style, 12);
            m_Window.style.overflow = Overflow.Hidden;
            m_Window.style.display = DisplayStyle.None;
            m_Window.Add(panel);
            m_Overlay.Add(m_Window);

            m_FloatingButton = CheatStyles.Label("CHEAT", 14f, bold: true);
            var s = m_FloatingButton.style;
            s.position = Position.Absolute;
            s.width = 76;
            s.height = 44;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.backgroundColor = new Color(CheatStyles.Accent.r, CheatStyles.Accent.g, CheatStyles.Accent.b, 0.85f);
            CheatStyles.Radius(s, 22);
            s.display = showFloatingButton ? DisplayStyle.Flex : DisplayStyle.None;
            m_FloatingButton.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_FloatingButton.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_FloatingButton.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Overlay.Add(m_FloatingButton);
            m_FloatingButton.userData = showFloatingButton;
        }

        public void AttachTo(VisualElement root)
        {
            Root = root;

            // 테마가 없으면 문서 루트 높이가 0이 되므로 화면 전체로 편다
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            m_Overlay.RemoveFromHierarchy();
            root.Add(m_Overlay);
        }

        public bool TryAttach(VisualElement panel) => true;

        public void Detach()
        {
            m_Overlay.RemoveFromHierarchy();
            Root = null;
        }

        public void Open()
        {
            m_IsOpen = true;
            m_Window.style.display = DisplayStyle.Flex;
            m_FloatingButton.style.display = DisplayStyle.None;
            m_Panel.Focus();
        }

        public void Close()
        {
            m_IsOpen = false;
            m_Window.style.display = DisplayStyle.None;
            if (m_FloatingButton.userData is bool show && show)
                m_FloatingButton.style.display = DisplayStyle.Flex;
            m_Panel.focusController?.focusedElement?.Blur();
        }

        public void SetSafeArea(float left, float top, float right, float bottom)
        {
            if (Mathf.Approximately(left, m_SafeLeft) && Mathf.Approximately(top, m_SafeTop)
                && Mathf.Approximately(right, m_SafeRight) && Mathf.Approximately(bottom, m_SafeBottom))
                return;

            m_SafeLeft = left;
            m_SafeTop = top;
            m_SafeRight = right;
            m_SafeBottom = bottom;
            Relayout();
        }

        /// <summary>넓은 화면(PC)은 오른쪽 60%, 그 외(모바일)는 안전 영역 안 전체</summary>
        private void Relayout()
        {
            float width = m_Overlay.layout.width;
            if (float.IsNaN(width)) return;

            var s = m_Window.style;
            s.top = m_SafeTop + WindowMargin;
            s.bottom = m_SafeBottom + WindowMargin;
            s.right = m_SafeRight + WindowMargin;
            if (width >= WideWindowMinWidth)
            {
                s.left = StyleKeyword.Auto;
                s.width = Length.Percent(60);
            }
            else
            {
                s.left = m_SafeLeft + WindowMargin;
                s.width = StyleKeyword.Auto;
            }

            if (!m_ButtonPlaced && width > 0)
            {
                m_ButtonPlaced = true;
                m_FloatingButton.style.left = width - 76 - m_SafeRight - 12;
                m_FloatingButton.style.top = m_Overlay.layout.height * 0.35f;
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            m_PointerDown = true;
            m_Dragging = false;
            m_PointerStart = evt.position;
            m_ButtonStart = new Vector2(m_FloatingButton.resolvedStyle.left, m_FloatingButton.resolvedStyle.top);
            m_FloatingButton.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!m_PointerDown || !m_FloatingButton.HasPointerCapture(evt.pointerId)) return;

            Vector2 delta = (Vector2)evt.position - m_PointerStart;
            if (!m_Dragging && delta.magnitude < DragThreshold) return;
            m_Dragging = true;

            float maxX = Mathf.Max(0, m_Overlay.layout.width - m_FloatingButton.layout.width);
            float maxY = Mathf.Max(0, m_Overlay.layout.height - m_FloatingButton.layout.height);
            m_FloatingButton.style.left = Mathf.Clamp(m_ButtonStart.x + delta.x, 0, maxX);
            m_FloatingButton.style.top = Mathf.Clamp(m_ButtonStart.y + delta.y, 0, maxY);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_PointerDown) return;
            m_PointerDown = false;
            m_FloatingButton.ReleasePointer(evt.pointerId);
            if (!m_Dragging) CheatSystem.Open();
        }
    }
}
