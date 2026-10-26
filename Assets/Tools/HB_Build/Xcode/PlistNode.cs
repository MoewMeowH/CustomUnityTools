using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// plist 값 하나 (iOS 모듈 없이 쓰는 순수 모델). 붙여넣기 · Raw 값 해석 · 합치기 · 기존 값 비교에 쓴다.
    /// &lt;data&gt;는 지원하지 않는다.
    /// </summary>
    public sealed class PlistNode
    {
        public enum EKind
        {
            String,
            Bool,
            Integer,
            Real,
            Date,
            Array,
            Dict,
        }

        public EKind Kind { get; }

        /// <summary> 스칼라 값 원문 (Bool은 "true" / "false") </summary>
        public string Text { get; }

        public List<PlistNode> Items { get; }

        /// <summary> 딕셔너리 항목 (순서 유지, 비교는 순서 무관) </summary>
        public List<KeyValuePair<string, PlistNode>> Entries { get; }

        private PlistNode(EKind kind, string text, List<PlistNode> items, List<KeyValuePair<string, PlistNode>> entries)
        {
            Kind = kind;
            Text = text;
            Items = items;
            Entries = entries;
        }

        public static PlistNode String(string value) => new PlistNode(EKind.String, value ?? string.Empty, null, null);
        public static PlistNode Bool(bool value) => new PlistNode(EKind.Bool, value ? "true" : "false", null, null);
        public static PlistNode Integer(long value) => new PlistNode(EKind.Integer, value.ToString(CultureInfo.InvariantCulture), null, null);
        public static PlistNode Real(double value) => new PlistNode(EKind.Real, value.ToString("R", CultureInfo.InvariantCulture), null, null);
        public static PlistNode Date(string iso8601) => new PlistNode(EKind.Date, iso8601 ?? string.Empty, null, null);
        public static PlistNode Array(IEnumerable<PlistNode> items) => new PlistNode(EKind.Array, null, items.ToList(), null);
        public static PlistNode StringArray(IEnumerable<string> items) => Array(items.Select(String));

        public static PlistNode Dict(IEnumerable<KeyValuePair<string, PlistNode>> entries) =>
            new PlistNode(EKind.Dict, null, null, entries.ToList());

        public bool IsScalar => Kind is not (EKind.Array or EKind.Dict);

        /// <summary> 모든 항목이 문자열인 배열인지 </summary>
        public bool IsStringArray => Kind == EKind.Array && Items.All(i => i.Kind == EKind.String);

        public bool BoolValue => Kind == EKind.Bool && Text == "true";

        public long IntegerValue => long.Parse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture);

        public double RealValue => double.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);

        public PlistNode Get(string key)
        {
            if (Kind != EKind.Dict) return null;
            foreach (var e in Entries)
            {
                if (e.Key == key) return e.Value;
            }
            return null;
        }

        /// <summary> 구조 비교. 딕셔너리는 순서 무관, 숫자는 값으로 비교한다 </summary>
        public bool ValueEquals(PlistNode other)
        {
            if (other == null || Kind != other.Kind) return false;
            switch (Kind)
            {
                case EKind.Integer:
                    return IntegerValue == other.IntegerValue;
                case EKind.Real:
                    return RealValue.Equals(other.RealValue);
                case EKind.Array:
                    if (Items.Count != other.Items.Count) return false;
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (!Items[i].ValueEquals(other.Items[i])) return false;
                    }
                    return true;
                case EKind.Dict:
                    if (Entries.Count != other.Entries.Count) return false;
                    foreach (var e in Entries)
                    {
                        var o = other.Get(e.Key);
                        if (o == null || !e.Value.ValueEquals(o)) return false;
                    }
                    return true;
                default:
                    return string.Equals(Text, other.Text, StringComparison.Ordinal);
            }
        }

        /// <summary> 로그 · 인스펙터 표시용 짧은 문자열 </summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case EKind.String:
                    return $"\"{Text}\"";
                case EKind.Array:
                    return "[" + string.Join(", ", Items.Select(i => i.ToString())) + "]";
                case EKind.Dict:
                    return "{" + string.Join(", ", Entries.Select(e => $"{e.Key}: {e.Value}")) + "}";
                default:
                    return Text;
            }
        }

        public string ToXml()
        {
            var sb = new StringBuilder();
            AppendXml(sb);
            return sb.ToString();
        }

        private void AppendXml(StringBuilder sb)
        {
            switch (Kind)
            {
                case EKind.String:
                    sb.Append("<string>").Append(SecurityElementEscape(Text)).Append("</string>");
                    break;
                case EKind.Bool:
                    sb.Append(BoolValue ? "<true/>" : "<false/>");
                    break;
                case EKind.Integer:
                    sb.Append("<integer>").Append(Text).Append("</integer>");
                    break;
                case EKind.Real:
                    sb.Append("<real>").Append(Text).Append("</real>");
                    break;
                case EKind.Date:
                    sb.Append("<date>").Append(SecurityElementEscape(Text)).Append("</date>");
                    break;
                case EKind.Array:
                    sb.Append("<array>");
                    foreach (var i in Items) i.AppendXml(sb);
                    sb.Append("</array>");
                    break;
                case EKind.Dict:
                    sb.Append("<dict>");
                    foreach (var e in Entries)
                    {
                        sb.Append("<key>").Append(SecurityElementEscape(e.Key)).Append("</key>");
                        e.Value.AppendXml(sb);
                    }
                    sb.Append("</dict>");
                    break;
            }
        }

        private static string SecurityElementEscape(string s) => System.Security.SecurityElement.Escape(s) ?? string.Empty;

        // ───────────── 파싱 ─────────────

        /// <summary> 값 하나의 XML (예: "&lt;array&gt;...&lt;/array&gt;")을 해석합니다. 형식이 틀리면 FormatException </summary>
        public static PlistNode ParseValueXml(string xml)
        {
            var elements = ParseElements(xml);
            if (elements.Count != 1)
                throw new FormatException($"Expected exactly one plist value, found {elements.Count}.");
            return FromElement(elements[0]);
        }

        /// <summary>
        /// SDK 문서의 plist 조각을 키 · 값 목록으로 해석합니다.
        /// 받는 형식: "&lt;key&gt;A&lt;/key&gt;&lt;string&gt;b&lt;/string&gt;..." 나열, &lt;dict&gt; 하나, 또는 전체 plist 파일.
        /// </summary>
        public static List<KeyValuePair<string, PlistNode>> ParseKeyValueFragment(string xml)
        {
            var elements = ParseElements(xml);

            // 전체 plist 또는 <dict> 하나면 그 안의 항목을 쓴다
            if (elements.Count == 1 && elements[0].Name.LocalName == "plist")
                elements = elements[0].Elements().ToList();
            if (elements.Count == 1 && elements[0].Name.LocalName == "dict")
                return FromElement(elements[0]).Entries;

            return ParsePairs(elements, "fragment");
        }

        private static List<XElement> ParseElements(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new FormatException("Empty plist XML.");

            // XML 선언 · DOCTYPE은 조각을 감싸면 위치가 틀려지므로 지운다
            string body = Regex.Replace(xml, @"<\?xml[^>]*\?>", string.Empty);
            body = Regex.Replace(body, @"<!DOCTYPE[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            try
            {
                var root = XElement.Parse("<root>" + body + "</root>", LoadOptions.None);
                return root.Elements().ToList();
            }
            catch (XmlException ex)
            {
                throw new FormatException($"Invalid plist XML: {ex.Message}", ex);
            }
        }

        private static List<KeyValuePair<string, PlistNode>> ParsePairs(List<XElement> elements, string where)
        {
            var result = new List<KeyValuePair<string, PlistNode>>();
            for (int i = 0; i < elements.Count; i++)
            {
                var keyEl = elements[i];
                if (keyEl.Name.LocalName != "key")
                    throw new FormatException($"Expected <key> in {where}, found <{keyEl.Name.LocalName}>.");
                if (i + 1 >= elements.Count)
                    throw new FormatException($"Key '{keyEl.Value}' in {where} has no value.");
                var valueEl = elements[++i];
                if (valueEl.Name.LocalName == "key")
                    throw new FormatException($"Key '{keyEl.Value}' in {where} has no value.");
                result.Add(new KeyValuePair<string, PlistNode>(keyEl.Value, FromElement(valueEl)));
            }
            return result;
        }

        private static PlistNode FromElement(XElement el)
        {
            switch (el.Name.LocalName)
            {
                case "string":
                    return String(el.Value);
                case "true":
                    return Bool(true);
                case "false":
                    return Bool(false);
                case "integer":
                    if (!long.TryParse(el.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                        throw new FormatException($"Invalid <integer>: '{el.Value}'.");
                    return Integer(l);
                case "real":
                    if (!double.TryParse(el.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        throw new FormatException($"Invalid <real>: '{el.Value}'.");
                    return Real(d);
                case "date":
                    return Date(el.Value.Trim());
                case "array":
                    return Array(el.Elements().Select(FromElement));
                case "dict":
                    var pairs = ParsePairs(el.Elements().ToList(), "<dict>");
                    var dup = pairs.GroupBy(p => p.Key).FirstOrDefault(g => g.Count() > 1);
                    if (dup != null)
                        throw new FormatException($"Duplicate key '{dup.Key}' in <dict>.");
                    return Dict(pairs);
                default:
                    throw new FormatException($"Unsupported plist element <{el.Name.LocalName}> (data is not supported).");
            }
        }
    }
}
