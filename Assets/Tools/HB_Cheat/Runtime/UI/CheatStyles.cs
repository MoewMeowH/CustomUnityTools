#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace HB.Cheat.UI
{
    /// <summary>
    /// 인라인 스타일. 빌드 제외를 위해 USS/테마 에셋 없이 코드로만 꾸민다 (테마가 없어도 동작하도록 ScrollView 내부까지 지정).
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CheatStyles
    {
        public static readonly Color Background = new Color(0.08f, 0.09f, 0.11f, 0.96f);
        public static readonly Color Surface = new Color(0.14f, 0.15f, 0.18f, 1f);
        public static readonly Color SurfaceHigh = new Color(0.20f, 0.22f, 0.26f, 1f);
        public static readonly Color Border = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Text = new Color(0.92f, 0.93f, 0.95f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.70f, 1f);
        public static readonly Color Accent = new Color(0.27f, 0.55f, 0.98f, 1f);
        public static readonly Color AccentDim = new Color(0.27f, 0.55f, 0.98f, 0.28f);
        public static readonly Color Success = new Color(0.30f, 0.78f, 0.45f, 1f);
        public static readonly Color Warning = new Color(0.98f, 0.72f, 0.25f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.36f, 0.36f, 1f);
        public static readonly Color Favorite = new Color(1f, 0.80f, 0.20f, 1f);

        /// <summary>터치 최소 높이 (패널 좌표)</summary>
        public const float TouchHeight = 44f;
        public const float FontSize = 16f;
        public const float SmallFontSize = 13f;

        public static void Radius(IStyle s, float r)
        {
            s.borderTopLeftRadius = r;
            s.borderTopRightRadius = r;
            s.borderBottomLeftRadius = r;
            s.borderBottomRightRadius = r;
        }

        public static void Padding(IStyle s, float vertical, float horizontal)
        {
            s.paddingTop = vertical;
            s.paddingBottom = vertical;
            s.paddingLeft = horizontal;
            s.paddingRight = horizontal;
        }

        public static void Margin(IStyle s, float all)
        {
            s.marginTop = all;
            s.marginBottom = all;
            s.marginLeft = all;
            s.marginRight = all;
        }

        public static void BorderColor(IStyle s, Color c, float width = 1f)
        {
            s.borderTopWidth = width;
            s.borderBottomWidth = width;
            s.borderLeftWidth = width;
            s.borderRightWidth = width;
            s.borderTopColor = c;
            s.borderBottomColor = c;
            s.borderLeftColor = c;
            s.borderRightColor = c;
        }

        public static Label Label(string text, float size = FontSize, Color? color = null, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color ?? Text;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            Margin(label.style, 0);
            Padding(label.style, 0, 0);
            return label;
        }

        public static Button Button(string text, System.Action onClick, Color? background = null)
        {
            var button = new Button(onClick) { text = text };
            var s = button.style;
            s.minHeight = TouchHeight;
            s.fontSize = FontSize;
            s.color = Text;
            s.backgroundColor = background ?? SurfaceHigh;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.whiteSpace = WhiteSpace.Normal;
            Radius(s, 8);
            Padding(s, 6, 12);
            Margin(s, 3);
            BorderColor(s, Border);
            return button;
        }

        /// <summary>선택 상태 표시 (탭·칩·치트 버튼 공통)</summary>
        public static void SetSelected(VisualElement element, bool selected, Color? normal = null)
        {
            element.style.backgroundColor = selected ? Accent : (normal ?? SurfaceHigh);
            BorderColor(element.style, selected ? Accent : Border);
        }

        public static VisualElement Row(bool wrap = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0;
            if (wrap) row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static VisualElement Card()
        {
            var card = new VisualElement();
            var s = card.style;
            s.backgroundColor = Surface;
            s.flexShrink = 0;
            Radius(s, 10);
            Padding(s, 8, 8);
            s.marginBottom = 8;
            return card;
        }

        /// <summary>테마 없이도 동작하는 세로 ScrollView (스크롤바 숨김: 터치 드래그·휠로 스크롤)</summary>
        public static ScrollView ScrollView()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            FixScrollView(sv);
            return sv;
        }

        public static void FixScrollView(ScrollView sv)
        {
            sv.style.flexGrow = 1;
            sv.style.flexShrink = 1;
            sv.style.minHeight = 0;
            sv.style.overflow = Overflow.Hidden;
            sv.touchScrollBehavior = UnityEngine.UIElements.ScrollView.TouchScrollBehavior.Clamped;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.contentViewport.style.overflow = Overflow.Hidden;
            sv.contentViewport.style.flexGrow = 1;
            sv.contentContainer.style.flexShrink = 0;
        }

        public static TextField TextField(string placeholder)
        {
            var field = new TextField();
            field.textEdition.placeholder = placeholder;
            var s = field.style;
            s.flexGrow = 1;
            s.minHeight = TouchHeight;
            s.fontSize = FontSize;
            s.color = Text;
            Margin(s, 3);

            // 테마가 없으면 입력 영역이 투명하므로 직접 칠한다
            var input = field.Q(className: UnityEngine.UIElements.TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
                input.style.color = Text;
                Radius(input.style, 8);
                Padding(input.style, 6, 10);
                BorderColor(input.style, Border);
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.style.flexGrow = 1;
            }
            return field;
        }

        public static Color StatusColor(CheatStatus status)
        {
            switch (status)
            {
                case CheatStatus.Succeeded: return Success;
                case CheatStatus.Failed: return Danger;
                case CheatStatus.TimedOut: return Warning;
                default: return TextDim;
            }
        }

        public static string StatusText(CheatStatus status)
        {
            switch (status)
            {
                // 내장 폰트에 없는 기호를 피한다
                case CheatStatus.Pending: return "Pending...";
                case CheatStatus.Succeeded: return "Success";
                case CheatStatus.Failed: return "Failed";
                case CheatStatus.TimedOut: return "Timeout";
                default: return status.ToString();
            }
        }
    }
}
