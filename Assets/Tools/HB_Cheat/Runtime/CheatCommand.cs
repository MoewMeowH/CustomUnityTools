using System;
using System.Globalization;
using System.Text;

namespace HB.Cheat
{
    /// <summary>
    /// 서버 치트 문자열 조립. 문화권과 무관하게(InvariantCulture) 직렬화하고, 공백·따옴표가 있는 문자열은 따옴표로 감싼다.
    /// 예: CheatCommand.Join("item_enchant", itemId, level) → "item_enchant 1001 15"
    /// </summary>
    public static class CheatCommand
    {
        public static string Join(string name, params object[] args)
        {
            var sb = new StringBuilder(name);
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    sb.Append(' ');
                    sb.Append(Format(args[i]));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 값 하나를 서버 문자열 형식으로 바꾼다.
        /// bool → true/false, enum → 정수 값, 숫자 → InvariantCulture, 문자열 → 필요하면 "..." (\" \\ 이스케이프)
        /// </summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "\"\"";
                case string s:
                    return QuoteIfNeeded(s);
                case bool b:
                    return b ? "true" : "false";
                case Enum e:
                    return Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return QuoteIfNeeded(value.ToString());
            }
        }

        private static string QuoteIfNeeded(string s)
        {
            if (s.Length == 0) return "\"\"";

            bool needsQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '"' || c == '\\')
                {
                    needsQuote = true;
                    break;
                }
            }
            if (!needsQuote) return s;

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
