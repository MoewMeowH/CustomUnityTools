using System.Collections.Generic;
using System.Linq;

namespace HB.CustomBuild.Editor
{
    /// <summary> SDK 문서의 plist 조각을 Info · Entitlements 행으로 바꿉니다 (붙여넣기) </summary>
    public static class XcodePlistPaste
    {
        /// <summary> 형식이 틀리면 System.FormatException </summary>
        public static List<XcodePlistEntry> ToEntries(string xml) =>
            PlistNode.ParseKeyValueFragment(xml).Select(kv => ToEntry(kv.Key, kv.Value)).ToList();

        public static XcodePlistEntry ToEntry(string key, PlistNode node)
        {
            var entry = new XcodePlistEntry { Key = key };
            switch (node.Kind)
            {
                case PlistNode.EKind.String:
                    entry.Type = EPlistValueType.String;
                    entry.Value = node.Text;
                    break;
                case PlistNode.EKind.Bool:
                    entry.Type = EPlistValueType.Bool;
                    entry.Value = node.Text;
                    break;
                case PlistNode.EKind.Integer:
                    entry.Type = EPlistValueType.Integer;
                    entry.Value = node.Text;
                    break;
                case PlistNode.EKind.Real:
                    entry.Type = EPlistValueType.Real;
                    entry.Value = node.Text;
                    break;
                default:
                    if (node.IsStringArray && key != XcodeSettingsPlanner.SKADNETWORK_ITEMS)
                    {
                        entry.Type = EPlistValueType.StringArray;
                        entry.Values = node.Items.Select(i => i.Text).ToList();
                    }
                    else
                    {
                        entry.Type = EPlistValueType.Raw;
                        entry.RawXml = node.ToXml();
                    }
                    break;
            }
            return entry;
        }
    }
}
