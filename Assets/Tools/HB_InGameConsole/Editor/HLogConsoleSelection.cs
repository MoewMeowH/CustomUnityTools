using System.Collections.Generic;
using System.Text;

namespace InGameConsoleSystem.Editor
{
    /// <summary>
    /// 에디터 HLog 콘솔의 로그 줄 다중 선택 · 복사 (창 상태와 분리한 계산 부분).
    /// 선택은 항목 참조로 들고, 복사 · 범위는 지금 보이는 목록 순서를 따른다 (필터로 가려진 줄은 빠진다).
    /// </summary>
    internal static class HLogConsoleSelection
    {
        /// <summary>보이는 목록에서 선택된 항목을 화면 순서대로</summary>
        public static List<HLogCore.LogEntry> InDisplayOrder(IReadOnlyList<HLogCore.LogEntry> visible, ICollection<HLogCore.LogEntry> selected)
        {
            var result = new List<HLogCore.LogEntry>();
            if (selected.Count == 0) return result;
            for (int i = 0; i < visible.Count; i++)
            {
                if (selected.Contains(visible[i])) result.Add(visible[i]);
            }
            return result;
        }

        /// <summary>Shift+클릭: 기준 줄부터 누른 줄까지. 기준이 목록에 없으면 누른 줄 하나만 고르고 false (호출부가 기준을 누른 줄로 바꾼다).</summary>
        public static bool SelectRange(IReadOnlyList<HLogCore.LogEntry> visible, HLogCore.LogEntry anchor, int clickedIndex, ISet<HLogCore.LogEntry> selected)
        {
            int anchorIndex = -1;
            if (anchor != null)
            {
                for (int i = 0; i < visible.Count; i++)
                {
                    if (ReferenceEquals(visible[i], anchor)) { anchorIndex = i; break; }
                }
            }
            bool found = anchorIndex >= 0;
            if (!found) anchorIndex = clickedIndex;

            int from = anchorIndex < clickedIndex ? anchorIndex : clickedIndex;
            int to = anchorIndex < clickedIndex ? clickedIndex : anchorIndex;
            selected.Clear();
            for (int i = from; i <= to; i++) selected.Add(visible[i]);
            return found;
        }

        /// <summary>
        /// 한 줄에 하나: "HH:mm:ss [Type] [Tag] 메시지" — 메시지 안의 줄바꿈은 목록 화면처럼 공백으로 바꾼다.
        /// withStack이면 메시지 원문과 스택을 붙이고 항목 사이를 빈 줄로 나눈다.
        /// </summary>
        public static string Format(IReadOnlyList<HLogCore.LogEntry> entries, bool withStack)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (i > 0) sb.Append('\n');
                if (withStack && i > 0) sb.Append('\n');

                sb.Append(e.TimeString).Append(" [").Append(e.LogType).Append(']');
                if (!string.IsNullOrEmpty(e.Tag)) sb.Append(" [").Append(e.Tag).Append(']');
                sb.Append(' ').Append(withStack ? e.Message : OneLine(e.Message));

                if (withStack && !string.IsNullOrEmpty(e.StackTrace))
                    sb.Append('\n').Append(e.StackTrace.TrimEnd());
            }
            return sb.ToString();
        }

        private static string OneLine(string message)
        {
            if (string.IsNullOrEmpty(message) || (message.IndexOf('\n') < 0 && message.IndexOf('\r') < 0)) return message;
            return message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
