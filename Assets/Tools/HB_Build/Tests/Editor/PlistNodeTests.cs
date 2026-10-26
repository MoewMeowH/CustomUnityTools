using System;
using System.Linq;
using HB.CustomBuild.Editor;
using NUnit.Framework;

namespace HB.CustomBuild.Tests
{
    /// <summary> plist 조각 해석 · 붙여넣기 변환 </summary>
    public class PlistNodeTests
    {
        [Test]
        public void Paste_KeyValueSequence_ToTypedEntries()
        {
            const string xml = "<key>NSUserTrackingUsageDescription</key><string>광고 개인화</string>" +
                               "<key>ITSAppUsesNonExemptEncryption</key><false/>" +
                               "<key>Count</key><integer>3</integer>" +
                               "<key>LSApplicationQueriesSchemes</key><array><string>fb</string><string>kakao</string></array>" +
                               "<key>Nested</key><dict><key>A</key><true/></dict>";

            var entries = XcodePlistPaste.ToEntries(xml);

            CollectionAssert.AreEqual(
                new[] { EPlistValueType.String, EPlistValueType.Bool, EPlistValueType.Integer, EPlistValueType.StringArray, EPlistValueType.Raw },
                entries.Select(e => e.Type));
            Assert.AreEqual("광고 개인화", entries[0].Value);
            Assert.AreEqual("false", entries[1].Value);
            CollectionAssert.AreEqual(new[] { "fb", "kakao" }, entries[3].Values);
            Assert.IsTrue(PlistNode.ParseValueXml(entries[4].RawXml).Get("A").BoolValue);
        }

        [Test]
        public void Paste_FullPlistWithDoctype_And_DictWrapper()
        {
            const string full = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                                "<plist version=\"1.0\"><dict><key>GADApplicationIdentifier</key><string>ca-app-pub-0~1</string></dict></plist>";
            Assert.AreEqual("GADApplicationIdentifier", XcodePlistPaste.ToEntries(full).Single().Key);
            Assert.AreEqual("B", XcodePlistPaste.ToEntries("<dict><key>B</key><string>x</string></dict>").Single().Key);
        }

        [Test]
        public void Paste_SkAdNetworkItems_StaysRaw()
        {
            var e = XcodePlistPaste.ToEntries("<key>SKAdNetworkItems</key><array><dict><key>SKAdNetworkIdentifier</key><string>a.skadnetwork</string></dict></array>").Single();
            Assert.AreEqual(EPlistValueType.Raw, e.Type);
        }

        [TestCase("<key>A</key>")]
        [TestCase("<string>x</string>")]
        [TestCase("<key>A</key><data>AAAA</data>")]
        [TestCase("<key>A</key><string>x")]
        [TestCase("")]
        public void Paste_Invalid_Throws(string xml)
        {
            Assert.Throws<FormatException>(() => XcodePlistPaste.ToEntries(xml));
        }

        [Test]
        public void ValueEquals_DictOrderInsensitive_NumbersByValue()
        {
            var a = PlistNode.ParseValueXml("<dict><key>x</key><integer>01</integer><key>y</key><real>1.50</real></dict>");
            var b = PlistNode.ParseValueXml("<dict><key>y</key><real>1.5</real><key>x</key><integer>1</integer></dict>");
            Assert.IsTrue(a.ValueEquals(b));
            Assert.IsFalse(a.ValueEquals(PlistNode.ParseValueXml("<dict><key>x</key><integer>2</integer><key>y</key><real>1.5</real></dict>")));
        }

        [Test]
        public void ToXml_RoundTrip_EscapesText()
        {
            var node = PlistNode.ParseValueXml("<array><string>a &lt; b &amp; c</string><true/><dict><key>k</key><integer>-5</integer></dict></array>");
            Assert.IsTrue(node.ValueEquals(PlistNode.ParseValueXml(node.ToXml())));
            Assert.AreEqual("a < b & c", node.Items[0].Text);
        }
    }
}
