using System;
using System.Globalization;

namespace HB.Cheat
{
    /// <summary>
    /// 인자 값 ↔ 문자열 변환 (자동 QA·Raw 입력용). 항상 InvariantCulture.
    /// </summary>
    public static class CheatValueParser
    {
        public static bool TryParse(CheatArgDefinition arg, string text, out object value, out string error)
        {
            value = null;
            error = null;
            text = text?.Trim() ?? string.Empty;
            var inv = CultureInfo.InvariantCulture;

            switch (arg.ValueType)
            {
                case CheatValueType.Int:
                    if (int.TryParse(text, NumberStyles.Integer, inv, out int i)) { value = i; return true; }
                    break;
                case CheatValueType.Long:
                    if (long.TryParse(text, NumberStyles.Integer, inv, out long l)) { value = l; return true; }
                    break;
                case CheatValueType.Float:
                    if (float.TryParse(text, NumberStyles.Float, inv, out float f)) { value = f; return true; }
                    break;
                case CheatValueType.Double:
                    if (double.TryParse(text, NumberStyles.Float, inv, out double d)) { value = d; return true; }
                    break;
                case CheatValueType.Bool:
                    if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
                    if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
                    break;
                case CheatValueType.String:
                    value = text;
                    return true;
                case CheatValueType.Enum:
                    if (arg.EnumType != null && Enum.TryParse(arg.EnumType, text, true, out object e) && Enum.IsDefined(arg.EnumType, e))
                    {
                        value = e;
                        return true;
                    }
                    break;
            }

            error = $"'{arg.Name}': '{text}' is not a valid {arg.ValueType}";
            return false;
        }

        public static string ToText(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case Enum e: return e.ToString();
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static object DefaultOf(CheatArgDefinition arg)
        {
            if (arg.DefaultValue != null) return arg.DefaultValue;

            // 목록 인자는 고를 때까지 비워 둔다 (0 같은 값이 목록에 없을 수 있음)
            if (arg.Provider != null) return null;

            switch (arg.ValueType)
            {
                case CheatValueType.Int: return arg.HasRange ? (int)Math.Max(arg.Min, Math.Min(arg.Max, 0)) : 0;
                case CheatValueType.Long: return arg.HasRange ? (long)Math.Max(arg.Min, Math.Min(arg.Max, 0)) : 0L;
                case CheatValueType.Float: return arg.HasRange ? (float)Math.Max(arg.Min, Math.Min(arg.Max, 0)) : 0f;
                case CheatValueType.Double: return arg.HasRange ? Math.Max(arg.Min, Math.Min(arg.Max, 0)) : 0d;
                case CheatValueType.Bool: return false;
                case CheatValueType.String: return string.Empty;
                case CheatValueType.Enum:
                    return arg.FixedOptions != null && arg.FixedOptions.Length > 0 ? arg.FixedOptions[0].Value : null;
                default: return null;
            }
        }

        /// <summary>숫자 인자를 double로 (범위 검사·스테퍼용)</summary>
        public static double ToDouble(object value)
        {
            switch (value)
            {
                case int i: return i;
                case long l: return l;
                case float f: return f;
                case double d: return d;
                default: return 0;
            }
        }

        /// <summary>double을 인자 타입으로 (정수형은 반올림, 범위 적용)</summary>
        public static object FromDouble(CheatArgDefinition arg, double value)
        {
            if (arg.HasRange) value = Math.Max(arg.Min, Math.Min(arg.Max, value));

            switch (arg.ValueType)
            {
                case CheatValueType.Int: return (int)Math.Round(Math.Max(int.MinValue, Math.Min(int.MaxValue, value)));
                case CheatValueType.Long: return (long)Math.Round(Math.Max(long.MinValue, Math.Min(long.MaxValue, value)));
                case CheatValueType.Float: return (float)value;
                default: return value;
            }
        }
    }
}
