using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HB.Cheat.UI
{
    /// <summary>
    /// 치트 패널 (독립 패널·콘솔 탭 공용).
    /// 명령은 문자열이 아니라 (치트, 인자 값 배열)로 들고 있고, 인자는 칩으로 표시한다.
    /// 칩을 누르면 그 인자의 편집기만 열린다. 숫자는 패널 키패드, OS 키보드는 검색·문자열 인자에서만 쓴다.
    /// </summary>
    public sealed class CheatPanel : VisualElement
    {
        private enum Mode
        {
            Browse,
            Favorites,
            History,
            Raw
        }

        private sealed class Node
        {
            public string Name;
            public CheatDefinition Cheat;
            public readonly List<Node> Children = new List<Node>();

            public Node FindGroup(string name)
            {
                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i].Cheat == null && Children[i].Name == name) return Children[i];
                }
                return null;
            }
        }

        private const float WideLayoutMinWidth = 640f;
        private const float OptionItemHeight = 48f;

        private readonly Button m_BrowseTab;
        private readonly Button m_FavoritesTab;
        private readonly Button m_HistoryTab;
        private readonly Button m_RawTab;
        private readonly Button m_CloseButton;
        private readonly TextField m_Search;

        private readonly VisualElement m_Body;
        private readonly VisualElement m_Nav;
        private readonly ScrollView m_NavScroll;
        private readonly VisualElement m_Composer;

        private readonly VisualElement m_EmptyHint;
        private readonly VisualElement m_ComposerContent;
        private readonly Label m_CheatTitle;
        private readonly Label m_CheatDescription;
        private readonly Button m_FavoriteToggle;
        private readonly VisualElement m_ChipRow;
        private readonly VisualElement m_EditorArea;
        // 숫자·토글·문자열 편집기가 들어가는 곳 (공간이 모자라면 스크롤)
        private VisualElement m_EditorParent;
        private readonly Label m_Preview;
        private readonly Button m_RunButton;
        private readonly Label m_Status;

        private Mode m_Mode = Mode.Browse;
        private Node m_Root;
        private readonly List<string> m_TabChain = new List<string>();
        private CheatDefinition m_Selected;
        private object[] m_Values;
        private int m_EditingArg = -1;
        private CheatExecution m_LastExecution;
        private bool m_Wide;

        // 숫자 편집기: 입력 중인 문자열 (null이면 현재 값 표시)
        private string m_NumberBuffer;
        private Label m_NumberValueLabel;
        private Label m_NumberRangeLabel;

        // 목록 편집기
        private readonly List<CheatOption> m_FilteredOptions = new List<CheatOption>();
        private IReadOnlyList<CheatOption> m_AllOptions = Array.Empty<CheatOption>();
        private ListView m_OptionList;
        private Label m_OptionCount;
        private string m_OptionQuery = string.Empty;

        // Raw
        private TextField m_RawField;
        private Label m_RawStatus;
        private CheatExecution m_LastRawExecution;

        private float m_SafeBottom;
        private float m_KeyboardBottom;

        public event Action CloseRequested;

        public CheatPanel()
        {
            name = "HBCheatPanel";
            focusable = true;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;
            style.backgroundColor = CheatStyles.Background;
            style.color = CheatStyles.Text;
            CheatStyles.Padding(style, 8, 8);

            // 헤더: 모드 탭 + 닫기
            var header = CheatStyles.Row(wrap: true);
            header.style.marginBottom = 4;
            var title = CheatStyles.Label("Cheat", 18f, bold: true);
            title.style.marginRight = 8;
            header.Add(title);
            m_BrowseTab = CheatStyles.Button("Browse", () => SetMode(Mode.Browse));
            m_FavoritesTab = CheatStyles.Button("Favorites", () => SetMode(Mode.Favorites));
            m_HistoryTab = CheatStyles.Button("History", () => SetMode(Mode.History));
            m_RawTab = CheatStyles.Button("Raw", () => SetMode(Mode.Raw));
            header.Add(m_BrowseTab);
            header.Add(m_FavoritesTab);
            header.Add(m_HistoryTab);
            header.Add(m_RawTab);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            header.Add(spacer);
            m_CloseButton = CheatStyles.Button("Close", () => CloseRequested?.Invoke(), CheatStyles.Surface);
            header.Add(m_CloseButton);
            Add(header);

            // 검색은 패널 상단에 둔다 (OS 키보드가 아래에서 올라와도 가리지 않도록)
            m_Search = CheatStyles.TextField("Search cheats (name / id)");
            m_Search.style.flexGrow = 0;
            m_Search.RegisterValueChangedCallback(_ => RebuildNav());
            Add(m_Search);

            m_Body = new VisualElement();
            m_Body.style.flexGrow = 1;
            m_Body.style.minHeight = 0;
            Add(m_Body);

            m_Nav = new VisualElement();
            m_Nav.style.minHeight = 0;
            m_NavScroll = CheatStyles.ScrollView();
            m_Nav.Add(m_NavScroll);
            m_Body.Add(m_Nav);

            m_Composer = new VisualElement();
            m_Composer.style.flexGrow = 1;
            m_Composer.style.minHeight = 0;
            m_Body.Add(m_Composer);

            m_EmptyHint = CheatStyles.Label("Select a cheat.", CheatStyles.FontSize, CheatStyles.TextDim);
            CheatStyles.Padding(m_EmptyHint.style, 12, 4);
            m_Composer.Add(m_EmptyHint);

            m_ComposerContent = new VisualElement();
            m_ComposerContent.style.flexGrow = 1;
            m_ComposerContent.style.minHeight = 0;
            m_Composer.Add(m_ComposerContent);

            var titleRow = CheatStyles.Row();
            m_CheatTitle = CheatStyles.Label(string.Empty, 17f, bold: true);
            m_CheatTitle.style.flexGrow = 1;
            m_CheatTitle.style.flexShrink = 1;
            titleRow.Add(m_CheatTitle);
            m_FavoriteToggle = CheatStyles.Button("+ Fav", ToggleFavorite, CheatStyles.Surface);
            titleRow.Add(m_FavoriteToggle);
            m_ComposerContent.Add(titleRow);

            m_CheatDescription = CheatStyles.Label(string.Empty, CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_ComposerContent.Add(m_CheatDescription);

            m_ChipRow = CheatStyles.Row(wrap: true);
            m_ChipRow.style.marginTop = 4;
            m_ComposerContent.Add(m_ChipRow);

            m_EditorArea = new VisualElement();
            m_EditorArea.style.flexGrow = 1;
            m_EditorArea.style.minHeight = 0;
            m_EditorArea.style.marginTop = 4;
            m_EditorArea.style.overflow = Overflow.Hidden;
            m_ComposerContent.Add(m_EditorArea);

            m_Preview = CheatStyles.Label(string.Empty, CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_Preview.style.marginTop = 4;
            m_Preview.selection.isSelectable = true;
            m_ComposerContent.Add(m_Preview);

            var runRow = CheatStyles.Row();
            m_RunButton = CheatStyles.Button("Run", Run, CheatStyles.Accent);
            m_RunButton.style.minWidth = 120;
            m_RunButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            runRow.Add(m_RunButton);
            m_Status = CheatStyles.Label(string.Empty, CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_Status.style.flexShrink = 1;
            m_Status.style.marginLeft = 8;
            runRow.Add(m_Status);
            m_ComposerContent.Add(runRow);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<AttachToPanelEvent>(_ => Subscribe());
            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());

            ApplyLayout();
            SetMode(Mode.Browse);
        }

        /// <summary>콘솔에 붙으면 콘솔의 닫기를 쓰므로 숨길 수 있다.</summary>
        public bool ShowCloseButton
        {
            set => m_CloseButton.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>테마 없이 쓸 때 기본 폰트 지정</summary>
        public void ApplyFallbackFont(Font font)
        {
            if (font != null) style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        /// <summary>안전 영역·OS 키보드 높이 (패널 좌표). 아래쪽 여백으로 반영한다.</summary>
        public void SetBottomInsets(float safeBottom, float keyboardBottom)
        {
            if (Mathf.Approximately(safeBottom, m_SafeBottom) && Mathf.Approximately(keyboardBottom, m_KeyboardBottom)) return;
            m_SafeBottom = safeBottom;
            m_KeyboardBottom = keyboardBottom;
            style.paddingBottom = 8 + Mathf.Max(safeBottom, keyboardBottom);
        }

        /// <summary>외부(히스토리, 자동화)에서 치트를 골라 패널에 띄운다.</summary>
        public void Show(CheatDefinition definition, object[] values = null)
        {
            if (definition == null) return;

            m_Search.SetValueWithoutNotify(string.Empty);
            SetMode(Mode.Browse);   // 탭 경로는 SelectCheat가 치트 위치로 맞춘다
            SelectCheat(definition, values);
        }

        #region Events

        private void Subscribe()
        {
            CheatRegistry.Changed -= OnRegistryChanged;
            CheatRegistry.Changed += OnRegistryChanged;
            CheatSystem.ExecutionChanged -= OnExecutionChanged;
            CheatSystem.ExecutionChanged += OnExecutionChanged;
            CheatSystem.FavoritesChanged -= OnFavoritesChanged;
            CheatSystem.FavoritesChanged += OnFavoritesChanged;
            CheatSystem.ServerQueueChanged -= OnServerQueueChanged;
            CheatSystem.ServerQueueChanged += OnServerQueueChanged;
            m_Root = null;
            RebuildNav();
        }

        private void Unsubscribe()
        {
            CheatRegistry.Changed -= OnRegistryChanged;
            CheatSystem.ExecutionChanged -= OnExecutionChanged;
            CheatSystem.FavoritesChanged -= OnFavoritesChanged;
            CheatSystem.ServerQueueChanged -= OnServerQueueChanged;
        }

        private void OnRegistryChanged()
        {
            m_Root = null;
            RebuildNav();
        }

        private void OnExecutionChanged(CheatExecution execution)
        {
            if (execution == m_LastExecution) RefreshStatus();
            if (execution == m_LastRawExecution) RefreshRawStatus();
            if (m_Mode == Mode.History) RebuildNav();
        }

        // 멈춤·재개는 대기 중인 건의 메시지만 바꾸므로 상태 줄과 히스토리를 다시 그린다
        private void OnServerQueueChanged()
        {
            RefreshStatus();
            RefreshRawStatus();
            if (m_Mode == Mode.History) RebuildNav();
        }

        private void OnFavoritesChanged()
        {
            RefreshFavoriteToggle();
            if (m_Mode == Mode.Favorites) RebuildNav();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            bool wide = evt.newRect.width >= WideLayoutMinWidth && evt.newRect.width > evt.newRect.height;
            if (wide == m_Wide) return;
            m_Wide = wide;
            ApplyLayout();
        }

        /// <summary>가로(PC·태블릿 가로)면 좌우 2단, 세로(모바일)면 위아래</summary>
        private void ApplyLayout()
        {
            if (m_Wide)
            {
                m_Body.style.flexDirection = FlexDirection.Row;
                m_Nav.style.width = Length.Percent(42);
                m_Nav.style.maxHeight = StyleKeyword.None;
                m_Nav.style.flexGrow = 0;
                m_Nav.style.flexShrink = 0;
                m_Nav.style.marginRight = 8;
                m_Nav.style.marginBottom = 0;
            }
            else
            {
                m_Body.style.flexDirection = FlexDirection.Column;
                m_Nav.style.width = StyleKeyword.Auto;
                m_Nav.style.maxHeight = Length.Percent(42);
                m_Nav.style.flexGrow = 0;
                m_Nav.style.flexShrink = 1;
                m_Nav.style.marginRight = 0;
                m_Nav.style.marginBottom = 8;
            }
        }

        #endregion

        #region Navigation

        private void SetMode(Mode mode)
        {
            m_Mode = mode;
            CheatStyles.SetSelected(m_BrowseTab, mode == Mode.Browse);
            CheatStyles.SetSelected(m_FavoritesTab, mode == Mode.Favorites);
            CheatStyles.SetSelected(m_HistoryTab, mode == Mode.History);
            CheatStyles.SetSelected(m_RawTab, mode == Mode.Raw);

            bool showComposer = mode == Mode.Browse || mode == Mode.Favorites;
            m_Composer.style.display = showComposer ? DisplayStyle.Flex : DisplayStyle.None;
            m_Search.style.display = showComposer ? DisplayStyle.Flex : DisplayStyle.None;

            // 히스토리·Raw는 nav 영역을 꽉 채운다
            if (showComposer) ApplyLayout();
            else
            {
                m_Nav.style.width = StyleKeyword.Auto;
                m_Nav.style.maxHeight = StyleKeyword.None;
                m_Nav.style.flexGrow = 1;
            }

            RebuildNav();
            RefreshComposer();
        }

        private Node Root => m_Root ??= BuildTree();

        private static Node BuildTree()
        {
            var root = new Node { Name = string.Empty };
            foreach (var def in CheatRegistry.All)
            {
                var segments = def.PathSegments;
                var node = root;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    var group = node.FindGroup(segments[i]);
                    if (group == null)
                    {
                        group = new Node { Name = segments[i] };
                        node.Children.Add(group);
                    }
                    node = group;
                }
                node.Children.Add(new Node { Name = def.Name, Cheat = def });
            }
            return root;
        }

        private void RebuildNav()
        {
            var content = m_NavScroll.contentContainer;
            content.Clear();

            switch (m_Mode)
            {
                case Mode.Browse:
                    if (string.IsNullOrWhiteSpace(m_Search.value)) BuildBrowse(content);
                    else BuildFlat(content, MatchesSearch);
                    break;
                case Mode.Favorites:
                    BuildFlat(content, def => CheatSystem.IsFavorite(def.Id) && MatchesSearch(def));
                    break;
                case Mode.History:
                    BuildHistory(content);
                    break;
                case Mode.Raw:
                    BuildRaw(content);
                    break;
            }
        }

        private void BuildBrowse(VisualElement content)
        {
            if (CheatRegistry.Count == 0)
            {
                content.Add(CheatStyles.Label("No cheats registered. Generate the registry in Tools > HB > Cheat.", CheatStyles.FontSize, CheatStyles.TextDim));
                return;
            }

            // 탭 줄을 깊이마다 하나씩: 그룹은 탭, 마지막 마디는 치트 버튼
            var node = Root;
            for (int depth = 0; ; depth++)
            {
                var card = CheatStyles.Card();
                var row = CheatStyles.Row(wrap: true);
                card.Add(row);
                content.Add(card);

                string selectedGroup = depth < m_TabChain.Count ? m_TabChain[depth] : null;
                foreach (var child in node.Children)
                {
                    if (child.Cheat == null)
                    {
                        string groupName = child.Name;
                        int capturedDepth = depth;
                        var tab = CheatStyles.Button(groupName + "  >", () => SelectTab(capturedDepth, groupName), CheatStyles.Surface);
                        tab.style.unityFontStyleAndWeight = FontStyle.Bold;
                        CheatStyles.SetSelected(tab, groupName == selectedGroup, CheatStyles.Surface);
                        row.Add(tab);
                    }
                    else
                    {
                        row.Add(CreateCheatButton(child.Cheat));
                    }
                }

                if (selectedGroup == null) break;
                var next = node.FindGroup(selectedGroup);
                if (next == null)
                {
                    m_TabChain.RemoveRange(depth, m_TabChain.Count - depth);
                    break;
                }
                node = next;
            }
        }

        private void BuildFlat(VisualElement content, Func<CheatDefinition, bool> filter)
        {
            var row = CheatStyles.Row(wrap: true);
            content.Add(row);
            int count = 0;
            foreach (var def in CheatRegistry.All)
            {
                if (!filter(def)) continue;
                var button = CreateCheatButton(def);
                button.text = def.Path;
                row.Add(button);
                count++;
            }
            if (count == 0)
                content.Add(CheatStyles.Label(m_Mode == Mode.Favorites ? "No favorites. Use '+ Fav' on a cheat." : "No match.", CheatStyles.FontSize, CheatStyles.TextDim));
        }

        private Button CreateCheatButton(CheatDefinition def)
        {
            var button = CheatStyles.Button(def.Name, () => SelectCheat(def, null));
            CheatStyles.SetSelected(button, def == m_Selected);
            if (def.Target == CheatTarget.Server)
            {
                button.style.borderLeftWidth = 4;
                button.style.borderLeftColor = CheatStyles.Warning;
            }
            return button;
        }

        private bool MatchesSearch(CheatDefinition def)
        {
            string query = m_Search.value;
            if (string.IsNullOrWhiteSpace(query)) return true;
            query = query.Trim();
            return def.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || def.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectTab(int depth, string group)
        {
            if (depth < m_TabChain.Count) m_TabChain.RemoveRange(depth, m_TabChain.Count - depth);
            m_TabChain.Add(group);
            RebuildNav();
        }

        private void BuildHistory(VisualElement content)
        {
            if (CheatSystem.IsServerQueuePaused)
            {
                var paused = CheatStyles.Card();
                paused.style.borderLeftWidth = 4;
                paused.style.borderLeftColor = CheatStyles.Warning;
                paused.Add(CheatStyles.Label($"Server queue paused — {CheatSystem.ServerQueuePauseReason}", CheatStyles.SmallFontSize, CheatStyles.Warning, bold: true));
                paused.Add(CheatStyles.Label(
                    $"{CheatSystem.QueuedServerCount} waiting. Resumes when the late response arrives. Resume only after reconnecting, or the late response will be taken as the next result.",
                    CheatStyles.SmallFontSize, CheatStyles.TextDim));
                var row = CheatStyles.Row();
                row.Add(CheatStyles.Button("Resume", CheatSystem.ResumeServerQueue, CheatStyles.Accent));
                paused.Add(row);
                content.Add(paused);
            }

            var history = CheatSystem.History;
            if (history.Count == 0)
            {
                content.Add(CheatStyles.Label("No history.", CheatStyles.FontSize, CheatStyles.TextDim));
                return;
            }

            for (int i = history.Count - 1; i >= 0; i--)
            {
                var execution = history[i];
                var card = CheatStyles.Card();
                card.style.borderLeftWidth = 4;
                card.style.borderLeftColor = CheatStyles.StatusColor(execution.Status);

                var headerRow = CheatStyles.Row();
                var statusLabel = CheatStyles.Label($"#{execution.Id}  {CheatStyles.StatusText(execution.Status)}", CheatStyles.SmallFontSize, CheatStyles.StatusColor(execution.Status), bold: true);
                statusLabel.style.flexGrow = 1;
                headerRow.Add(statusLabel);
                headerRow.Add(CheatStyles.Button(execution.Definition != null ? "Edit" : "Reuse", () => ReuseExecution(execution), CheatStyles.SurfaceHigh));
                card.Add(headerRow);

                var describe = CheatStyles.Label(execution.Describe());
                describe.selection.isSelectable = true;
                card.Add(describe);
                if (execution.Command != null && execution.Definition != null)
                    card.Add(CheatStyles.Label("> " + execution.Command, CheatStyles.SmallFontSize, CheatStyles.TextDim));
                if (!string.IsNullOrEmpty(execution.Message))
                    card.Add(CheatStyles.Label(execution.Message, CheatStyles.SmallFontSize, CheatStyles.TextDim));
                content.Add(card);
            }
        }

        private void ReuseExecution(CheatExecution execution)
        {
            if (execution.Definition != null)
            {
                Show(execution.Definition, (object[])execution.Values.Clone());
            }
            else
            {
                SetMode(Mode.Raw);
                m_RawField?.SetValueWithoutNotify(execution.Command ?? string.Empty);
            }
        }

        private void BuildRaw(VisualElement content)
        {
            var card = CheatStyles.Card();
            card.Add(CheatStyles.Label("Send a raw command string to the server as is.", CheatStyles.SmallFontSize, CheatStyles.TextDim));

            string previous = m_RawField?.value ?? string.Empty;
            m_RawField = CheatStyles.TextField("e.g. item_add 1001 10");
            m_RawField.SetValueWithoutNotify(previous);
            card.Add(m_RawField);

            var row = CheatStyles.Row();
            row.Add(CheatStyles.Button("Send", SendRaw, CheatStyles.Accent));
            m_RawStatus = CheatStyles.Label(string.Empty, CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_RawStatus.style.marginLeft = 8;
            m_RawStatus.style.flexShrink = 1;
            row.Add(m_RawStatus);
            card.Add(row);
            content.Add(card);
            RefreshRawStatus();
        }

        private void SendRaw()
        {
            if (m_RawField == null || string.IsNullOrWhiteSpace(m_RawField.value)) return;
            m_LastRawExecution = CheatSystem.ExecuteRaw(m_RawField.value);
            RefreshRawStatus();
        }

        private void RefreshRawStatus()
        {
            if (m_RawStatus == null) return;
            var e = m_LastRawExecution;
            m_RawStatus.text = e == null ? string.Empty : $"#{e.Id} {CheatStyles.StatusText(e.Status)} {e.Message}";
            m_RawStatus.style.color = e == null ? CheatStyles.TextDim : CheatStyles.StatusColor(e.Status);
        }

        #endregion

        #region Composer

        private void SelectCheat(CheatDefinition def, object[] values)
        {
            m_Selected = def;

            // 열린 탭을 이 치트의 부모 경로로 맞춘다 (더 깊이 열려 있던 하위 탭이 선택된 채 남지 않도록)
            m_TabChain.Clear();
            var segments = def.PathSegments;
            for (int i = 0; i < segments.Length - 1; i++)
                m_TabChain.Add(segments[i]);

            m_Values = values ?? CheatSystem.GetLastValues(def.Id) ?? def.CreateDefaultValues();
            if (m_Values.Length != def.Args.Length) m_Values = def.CreateDefaultValues();
            m_LastExecution = null;

            // 비어 있는 첫 인자부터 편집
            m_EditingArg = def.Args.Length > 0 ? 0 : -1;
            for (int i = 0; i < m_Values.Length; i++)
            {
                if (m_Values[i] == null)
                {
                    m_EditingArg = i;
                    break;
                }
            }

            RebuildNav();
            RefreshComposer();
        }

        private void RefreshComposer()
        {
            bool hasCheat = m_Selected != null;
            m_EmptyHint.style.display = hasCheat ? DisplayStyle.None : DisplayStyle.Flex;
            m_ComposerContent.style.display = hasCheat ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasCheat) return;

            m_CheatTitle.text = m_Selected.Path + (m_Selected.Target == CheatTarget.Server ? "  [Server]" : "  [Local]");
            m_CheatDescription.text = m_Selected.Description ?? string.Empty;
            m_CheatDescription.style.display = string.IsNullOrEmpty(m_Selected.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshFavoriteToggle();
            RebuildChips();
            RebuildEditor();
            RefreshPreview();
            RefreshStatus();
        }

        private void RefreshFavoriteToggle()
        {
            if (m_Selected == null) return;
            bool favorite = CheatSystem.IsFavorite(m_Selected.Id);
            m_FavoriteToggle.text = favorite ? "- Fav" : "+ Fav";
            m_FavoriteToggle.style.color = favorite ? CheatStyles.Favorite : CheatStyles.Text;
        }

        private void ToggleFavorite()
        {
            if (m_Selected == null) return;
            CheatSystem.SetFavorite(m_Selected.Id, !CheatSystem.IsFavorite(m_Selected.Id));
        }

        private void RebuildChips()
        {
            m_ChipRow.Clear();
            if (m_Selected.Args.Length == 0)
            {
                m_ChipRow.Add(CheatStyles.Label("No arguments.", CheatStyles.SmallFontSize, CheatStyles.TextDim));
                return;
            }

            for (int i = 0; i < m_Selected.Args.Length; i++)
            {
                int index = i;
                var arg = m_Selected.Args[i];
                var chip = CheatStyles.Button($"{arg.DisplayLabel}: {DisplayValue(index)}", () => EditArg(index));
                CheatStyles.SetSelected(chip, index == m_EditingArg);
                if (m_Values[index] == null)
                {
                    chip.style.borderBottomColor = CheatStyles.Danger;
                    chip.style.borderBottomWidth = 2;
                }
                m_ChipRow.Add(chip);
            }
        }

        private string DisplayValue(int index)
        {
            object value = m_Values[index];
            if (value == null) return "(select)";

            var arg = m_Selected.Args[index];
            if (arg.HasOptions)
            {
                var options = CheatSystem.GetOptions(m_Selected, index, m_Values, out _);
                for (int i = 0; i < options.Count; i++)
                {
                    if (Equals(options[i].Value, value)) return options[i].Label;
                }
            }
            return CheatValueParser.ToText(value);
        }

        private void EditArg(int index)
        {
            if (m_EditingArg == index) return;
            m_EditingArg = index;
            RebuildChips();
            RebuildEditor();
        }

        private void SetValue(int index, object value, bool rebuildEditor)
        {
            m_Values[index] = value;

            // 이 인자에 의존하는 뒤 인자는 초기화
            for (int j = index + 1; j < m_Selected.Args.Length; j++)
            {
                if (Array.IndexOf(m_Selected.Args[j].DependsOn, index) >= 0)
                    m_Values[j] = CheatValueParser.DefaultOf(m_Selected.Args[j]);
            }

            RebuildChips();
            if (rebuildEditor) RebuildEditor();
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (m_Selected == null) return;

            bool valid = CheatSystem.Validate(m_Selected, m_Values, out string error);
            if (m_Selected.Target == CheatTarget.Server)
            {
                string command = null;
                if (valid)
                {
                    try { command = m_Selected.ServerCommandBuilder?.Invoke(new CheatArgs(m_Values)); }
                    catch (Exception e) { error = e.Message; valid = false; }
                }
                m_Preview.text = valid ? "Send: " + command : "Send: (incomplete) " + error;
            }
            else
            {
                m_Preview.text = valid ? "Run: " + DescribeCurrent() : "Run: (incomplete) " + error;
            }
            m_Preview.style.color = valid ? CheatStyles.TextDim : CheatStyles.Danger;
            m_RunButton.SetEnabled(valid);
            m_RunButton.style.opacity = valid ? 1f : 0.45f;
        }

        private string DescribeCurrent()
        {
            var sb = new System.Text.StringBuilder(m_Selected.Path);
            for (int i = 0; i < m_Selected.Args.Length; i++)
                sb.Append(' ').Append(m_Selected.Args[i].Name).Append('=').Append(CheatValueParser.ToText(m_Values[i]));
            return sb.ToString();
        }

        private void Run()
        {
            if (m_Selected == null) return;
            m_LastExecution = CheatSystem.Execute(m_Selected, m_Values);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var e = m_LastExecution;
            if (e == null)
            {
                m_Status.text = string.Empty;
                return;
            }
            m_Status.text = $"#{e.Id} {CheatStyles.StatusText(e.Status)}" + (string.IsNullOrEmpty(e.Message) ? string.Empty : " - " + e.Message);
            m_Status.style.color = CheatStyles.StatusColor(e.Status);
        }

        #endregion

        #region Arg Editors

        private void RebuildEditor()
        {
            m_EditorArea.Clear();
            m_OptionList = null;
            m_NumberValueLabel = null;
            m_NumberBuffer = null;

            if (m_Selected == null || m_EditingArg < 0 || m_EditingArg >= m_Selected.Args.Length) return;

            var arg = m_Selected.Args[m_EditingArg];
            if (arg.Editor != CheatEditorKind.Options)
            {
                var scroll = CheatStyles.ScrollView();
                m_EditorArea.Add(scroll);
                m_EditorParent = scroll.contentContainer;
            }

            switch (arg.Editor)
            {
                case CheatEditorKind.Options:
                    BuildOptionsEditor(arg);
                    break;
                case CheatEditorKind.Number:
                    BuildNumberEditor(arg);
                    break;
                case CheatEditorKind.Toggle:
                    BuildToggleEditor();
                    break;
                default:
                    BuildTextEditor();
                    break;
            }
        }

        private void BuildOptionsEditor(CheatArgDefinition arg)
        {
            m_AllOptions = CheatSystem.GetOptions(m_Selected, m_EditingArg, m_Values, out string error);

            var searchRow = CheatStyles.Row();
            var search = CheatStyles.TextField($"Search {arg.DisplayLabel}");
            search.SetValueWithoutNotify(m_OptionQuery);
            search.RegisterValueChangedCallback(evt =>
            {
                m_OptionQuery = evt.newValue ?? string.Empty;
                FilterOptions();
            });
            searchRow.Add(search);
            m_OptionCount = CheatStyles.Label(string.Empty, CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_OptionCount.style.marginLeft = 6;
            searchRow.Add(m_OptionCount);
            m_EditorArea.Add(searchRow);

            if (error != null)
                m_EditorArea.Add(CheatStyles.Label(error, CheatStyles.SmallFontSize, CheatStyles.Danger));

            // 수만 개도 버티도록 가상화 리스트
            m_OptionList = new ListView
            {
                fixedItemHeight = OptionItemHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                itemsSource = m_FilteredOptions,
                makeItem = MakeOptionItem,
                bindItem = BindOptionItem
            };
            m_OptionList.style.flexGrow = 1;
            m_OptionList.style.minHeight = OptionItemHeight * 3;
            var innerScroll = m_OptionList.Q<ScrollView>();
            if (innerScroll != null) CheatStyles.FixScrollView(innerScroll);
            m_EditorArea.Add(m_OptionList);

            FilterOptions();
        }

        private VisualElement MakeOptionItem()
        {
            var button = CheatStyles.Button(string.Empty, null);
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.height = OptionItemHeight - 4;
            button.style.minHeight = OptionItemHeight - 4;
            button.clicked += () =>
            {
                if (button.userData is int index) ChooseOption(index);
            };
            return button;
        }

        private void BindOptionItem(VisualElement element, int index)
        {
            var button = (Button)element;
            var option = m_FilteredOptions[index];
            button.userData = index;
            string valueText = CheatValueParser.ToText(option.Value);
            button.text = option.Label == valueText ? option.Label : $"{option.Label}   ({valueText})";
            CheatStyles.SetSelected(button, m_EditingArg >= 0 && Equals(m_Values[m_EditingArg], option.Value));
        }

        private void FilterOptions()
        {
            m_FilteredOptions.Clear();
            string query = m_OptionQuery.Trim();
            for (int i = 0; i < m_AllOptions.Count; i++)
            {
                var option = m_AllOptions[i];
                if (query.Length == 0
                    || option.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || CheatValueParser.ToText(option.Value).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    m_FilteredOptions.Add(option);
                }
            }

            if (m_OptionCount != null) m_OptionCount.text = $"{m_FilteredOptions.Count}/{m_AllOptions.Count}";
            m_OptionList?.RefreshItems();
        }

        private void ChooseOption(int filteredIndex)
        {
            if (filteredIndex < 0 || filteredIndex >= m_FilteredOptions.Count) return;

            int arg = m_EditingArg;
            m_OptionQuery = string.Empty;
            SetValue(arg, m_FilteredOptions[filteredIndex].Value, rebuildEditor: false);

            // 다음 인자로 넘어간다
            m_EditingArg = arg + 1 < m_Selected.Args.Length ? arg + 1 : arg;
            RebuildChips();
            RebuildEditor();
        }

        private void BuildNumberEditor(CheatArgDefinition arg)
        {
            var card = CheatStyles.Card();
            m_NumberValueLabel = CheatStyles.Label(string.Empty, 26f, bold: true);
            m_NumberValueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(m_NumberValueLabel);
            m_NumberRangeLabel = CheatStyles.Label(arg.HasRange ? $"[{arg.Min} ~ {arg.Max}]  step {arg.Step}" : $"step {arg.Step}", CheatStyles.SmallFontSize, CheatStyles.TextDim);
            m_NumberRangeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(m_NumberRangeLabel);

            var steps = CheatStyles.Row();
            steps.style.justifyContent = Justify.Center;
            double step = arg.Step > 0 ? arg.Step : 1;
            steps.Add(StepButton("-" + Format(step * 10), -step * 10));
            steps.Add(StepButton("-" + Format(step), -step));
            steps.Add(StepButton("+" + Format(step), step));
            steps.Add(StepButton("+" + Format(step * 10), step * 10));
            if (arg.HasRange)
            {
                steps.Add(CheatStyles.Button("Min", () => SetNumber(arg.Min), CheatStyles.Surface));
                steps.Add(CheatStyles.Button("Max", () => SetNumber(arg.Max), CheatStyles.Surface));
            }
            card.Add(steps);

            // 패널 내 키패드 (OS 키보드를 띄우지 않는다)
            string[] keys = arg.IsInteger
                ? new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "+/-", "0", "<-", "C" }
                : new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "+/-", "0", ".", "<-", "C" };
            var pad = CheatStyles.Row(wrap: true);
            pad.style.justifyContent = Justify.Center;
            // 너비를 고정해야 버튼의 % 너비가 3열로 계산된다
            pad.style.width = 360;
            pad.style.maxWidth = Length.Percent(100);
            pad.style.alignSelf = Align.Center;
            foreach (var key in keys)
            {
                string captured = key;
                var button = CheatStyles.Button(key, () => PressKey(captured));
                button.style.width = Length.Percent(30);
                button.style.minHeight = CheatStyles.TouchHeight;
                pad.Add(button);
            }
            card.Add(pad);
            m_EditorParent.Add(card);

            RefreshNumberLabel();
        }

        private Button StepButton(string text, double delta)
        {
            return CheatStyles.Button(text, () =>
            {
                double current = m_Values[m_EditingArg] != null ? CheatValueParser.ToDouble(m_Values[m_EditingArg]) : 0;
                SetNumber(current + delta);
            });
        }

        private void SetNumber(double value)
        {
            var arg = m_Selected.Args[m_EditingArg];
            m_NumberBuffer = null;
            SetValue(m_EditingArg, CheatValueParser.FromDouble(arg, value), rebuildEditor: false);
            RefreshNumberLabel();
        }

        private void PressKey(string key)
        {
            var arg = m_Selected.Args[m_EditingArg];
            string buffer = m_NumberBuffer ?? string.Empty;

            switch (key)
            {
                case "C":
                    buffer = string.Empty;
                    break;
                case "<-":
                    if (m_NumberBuffer == null) buffer = CheatValueParser.ToText(m_Values[m_EditingArg]);
                    if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1);
                    break;
                case "+/-":
                    if (m_NumberBuffer == null) buffer = CheatValueParser.ToText(m_Values[m_EditingArg]);
                    buffer = buffer.StartsWith("-") ? buffer.Substring(1) : "-" + buffer;
                    break;
                case ".":
                    if (!buffer.Contains(".")) buffer = (buffer.Length == 0 || buffer == "-" ? buffer + "0" : buffer) + ".";
                    break;
                default:
                    buffer += key;
                    break;
            }

            m_NumberBuffer = buffer;
            if (CheatValueParser.TryParse(arg, buffer, out object parsed, out _))
                SetValue(m_EditingArg, parsed, rebuildEditor: false);
            else
                SetValue(m_EditingArg, null, rebuildEditor: false);
            RefreshNumberLabel();
        }

        private void RefreshNumberLabel()
        {
            if (m_NumberValueLabel == null) return;
            var arg = m_Selected.Args[m_EditingArg];
            object value = m_Values[m_EditingArg];
            m_NumberValueLabel.text = m_NumberBuffer ?? (value != null ? CheatValueParser.ToText(value) : "-");

            bool outOfRange = value != null && arg.HasRange && (CheatValueParser.ToDouble(value) < arg.Min || CheatValueParser.ToDouble(value) > arg.Max);
            m_NumberValueLabel.style.color = value == null || outOfRange ? CheatStyles.Danger : CheatStyles.Text;
        }

        private static string Format(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private void BuildToggleEditor()
        {
            var row = CheatStyles.Row();
            bool current = m_Values[m_EditingArg] is bool b && b;
            var on = CheatStyles.Button("True", () => SetValue(m_EditingArg, true, rebuildEditor: true));
            var off = CheatStyles.Button("False", () => SetValue(m_EditingArg, false, rebuildEditor: true));
            on.style.minWidth = 120;
            off.style.minWidth = 120;
            CheatStyles.SetSelected(on, current);
            CheatStyles.SetSelected(off, !current);
            row.Add(on);
            row.Add(off);
            m_EditorParent.Add(row);
        }

        private void BuildTextEditor()
        {
            var field = CheatStyles.TextField(m_Selected.Args[m_EditingArg].DisplayLabel);
            field.style.flexGrow = 0;
            field.SetValueWithoutNotify(m_Values[m_EditingArg] as string ?? string.Empty);
            field.RegisterValueChangedCallback(evt => SetValue(m_EditingArg, evt.newValue ?? string.Empty, rebuildEditor: false));
            m_EditorParent.Add(field);
        }

        #endregion
    }
}
