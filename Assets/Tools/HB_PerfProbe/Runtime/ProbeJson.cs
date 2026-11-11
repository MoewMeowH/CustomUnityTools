using System.Globalization;
using System.Text;

namespace HB.PerfProbe
{
    /// <summary>
    /// 작은 JSON 쓰기 도우미. 쉼표는 직접 넣지 않고 Key · Value 호출 순서로 붙인다.
    /// 숫자는 InvariantCulture로 쓴다 (기기 로캘이 소수점에 쉼표를 써도 PC 쪽 파싱이 깨지지 않게).
    /// </summary>
    internal sealed class ProbeJson
    {
        readonly StringBuilder _sb;
        bool _needComma;

        public ProbeJson(StringBuilder sb = null)
        {
            _sb = sb ?? new StringBuilder(256);
        }

        public ProbeJson BeginObject() { Sep(); _sb.Append('{'); _needComma = false; return this; }
        public ProbeJson EndObject() { _sb.Append('}'); _needComma = true; return this; }
        public ProbeJson BeginArray() { Sep(); _sb.Append('['); _needComma = false; return this; }
        public ProbeJson EndArray() { _sb.Append(']'); _needComma = true; return this; }

        public ProbeJson Key(string name)
        {
            Sep();
            WriteString(name);
            _sb.Append(':');
            _needComma = false;
            return this;
        }

        public ProbeJson Value(string value)
        {
            Sep();
            if (value == null) _sb.Append("null");
            else WriteString(value);
            _needComma = true;
            return this;
        }

        public ProbeJson Value(bool value) { Sep(); _sb.Append(value ? "true" : "false"); _needComma = true; return this; }
        public ProbeJson Value(long value) { Sep(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); _needComma = true; return this; }

        /// <summary>소수 셋째 자리까지. NaN · 무한대는 JSON에 없으므로 null.</summary>
        public ProbeJson Value(double value)
        {
            Sep();
            if (double.IsNaN(value) || double.IsInfinity(value)) _sb.Append("null");
            else _sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            _needComma = true;
            return this;
        }

        public ProbeJson Prop(string name, string value) => Key(name).Value(value);
        public ProbeJson Prop(string name, bool value) => Key(name).Value(value);
        public ProbeJson Prop(string name, long value) => Key(name).Value(value);
        public ProbeJson Prop(string name, double value) => Key(name).Value(value);

        public override string ToString() => _sb.ToString();

        void Sep()
        {
            if (_needComma) _sb.Append(',');
        }

        void WriteString(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public static string Error(string command, string message)
        {
            return new ProbeJson().BeginObject().Prop("ok", false).Prop("cmd", command).Prop("error", message).EndObject().ToString();
        }
    }
}
