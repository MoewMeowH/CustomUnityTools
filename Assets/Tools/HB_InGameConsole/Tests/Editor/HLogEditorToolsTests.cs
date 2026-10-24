using System.Collections.Generic;
using InGameConsoleSystem.Editor;
using NUnit.Framework;
using UnityEngine;

namespace InGameConsoleSystem.Tests
{
    /// <summary>USE_HLOG 자동 추가 판정, 릴리즈 빌드 경고 판정, 콘솔 로그 다중 선택 · 복사 문자열</summary>
    public class HLogEditorToolsTests
    {
        private const string Symbol = "USE_HLOG";

        [TestCase(false, false, false, "Add")]
        [TestCase(false, false, true, "MarkOnly")]
        [TestCase(true, false, false, "None")]
        [TestCase(true, false, true, "None")]
        [TestCase(false, true, false, "None")]
        [TestCase(false, true, true, "None")]
        public void Decide_AddsOnlyOnFirstInstallOutsideBatchMode(bool markerExists, bool batchMode, bool existingInstall, string expected)
        {
            Assert.AreEqual(expected, HLogDefineSetup.Decide(markerExists, batchMode, existingInstall).ToString());
        }

        [Test]
        public void WithSymbol_AppendsKeepingOrder()
        {
            CollectionAssert.AreEqual(new[] { "A", "B", Symbol }, HLogDefineSetup.WithSymbol(new[] { "A", "B" }, Symbol));
            CollectionAssert.AreEqual(new[] { Symbol }, HLogDefineSetup.WithSymbol(null, Symbol));
        }

        [Test]
        public void WithSymbol_AlreadyPresent_ReturnsNull()
        {
            Assert.IsNull(HLogDefineSetup.WithSymbol(new[] { "A", Symbol }, Symbol));
        }

        [Test]
        public void ReleaseCheck_NoSymbol_NoWarning()
        {
            Assert.IsFalse(HLogReleaseBuildCheck.IsReleaseWithHLog(false, new HashSet<string> { "LIVE" }, out _));
        }

        [Test]
        public void ReleaseCheck_DevelopmentWithoutLive_NoWarning()
        {
            Assert.IsFalse(HLogReleaseBuildCheck.IsReleaseWithHLog(true, new HashSet<string> { Symbol }, out _));
        }

        [Test]
        public void ReleaseCheck_NonDevelopment_Warns()
        {
            Assert.IsTrue(HLogReleaseBuildCheck.IsReleaseWithHLog(false, new HashSet<string> { Symbol }, out string reason));
            Assert.AreEqual("non-development build", reason);
        }

        [Test]
        public void ReleaseCheck_LiveDefine_WarnsEvenForDevelopment()
        {
            Assert.IsTrue(HLogReleaseBuildCheck.IsReleaseWithHLog(true, new HashSet<string> { Symbol, "LIVE" }, out string reason));
            Assert.AreEqual("define LIVE", reason);
        }

        private static List<HLogCore.LogEntry> Entries(int count)
        {
            var list = new List<HLogCore.LogEntry>();
            for (int i = 0; i < count; i++)
                list.Add(new HLogCore.LogEntry("message " + i, "at Foo.Bar () " + i, i % 2 == 0 ? LogType.Log : LogType.Warning));
            return list;
        }

        [Test]
        public void InDisplayOrder_FollowsVisibleListNotSelectionOrder()
        {
            var visible = Entries(4);
            var selected = new HashSet<HLogCore.LogEntry> { visible[3], visible[0], visible[2] };
            CollectionAssert.AreEqual(new[] { visible[0], visible[2], visible[3] }, HLogConsoleSelection.InDisplayOrder(visible, selected));
        }

        [Test]
        public void InDisplayOrder_SkipsEntriesNotVisible()
        {
            var visible = Entries(2);
            var hidden = Entries(1)[0];
            var selected = new HashSet<HLogCore.LogEntry> { hidden, visible[1] };
            CollectionAssert.AreEqual(new[] { visible[1] }, HLogConsoleSelection.InDisplayOrder(visible, selected));
        }

        [TestCase(1, 3)]
        [TestCase(3, 1)]
        public void SelectRange_BothDirections(int anchor, int clicked)
        {
            var visible = Entries(5);
            var selected = new HashSet<HLogCore.LogEntry> { visible[4] };
            Assert.IsTrue(HLogConsoleSelection.SelectRange(visible, visible[anchor], clicked, selected));
            CollectionAssert.AreEquivalent(new[] { visible[1], visible[2], visible[3] }, selected);
        }

        [Test]
        public void SelectRange_AnchorNotVisible_SelectsClickedOnly()
        {
            var visible = Entries(3);
            var selected = new HashSet<HLogCore.LogEntry>();
            Assert.IsFalse(HLogConsoleSelection.SelectRange(visible, Entries(1)[0], 2, selected));
            CollectionAssert.AreEquivalent(new[] { visible[2] }, selected);
        }

        [Test]
        public void Format_OneLinePerEntry()
        {
            var e = Entries(2);
            string expected = Line(e[0]) + "\n" + Line(e[1]);
            Assert.AreEqual(expected, HLogConsoleSelection.Format(e, withStack: false));
        }

        [Test]
        public void Format_WithStack_SeparatesEntriesByBlankLine()
        {
            var e = Entries(2);
            string expected = Line(e[0]) + "\n" + e[0].StackTrace + "\n\n" + Line(e[1]) + "\n" + e[1].StackTrace;
            Assert.AreEqual(expected, HLogConsoleSelection.Format(e, withStack: true));
        }

        [Test]
        public void Format_MultiLineMessage_FlattenedWithoutStack_KeptWithStack()
        {
            var e = new List<HLogCore.LogEntry> { new HLogCore.LogEntry("line1\nline2\r\nline3", "at X ()", LogType.Log) };
            string head = $"{e[0].TimeString} [{e[0].LogType}]" + (string.IsNullOrEmpty(e[0].Tag) ? "" : $" [{e[0].Tag}]");
            Assert.AreEqual(head + " line1 line2 line3", HLogConsoleSelection.Format(e, withStack: false));
            Assert.AreEqual(head + " " + e[0].Message + "\n" + "at X ()", HLogConsoleSelection.Format(e, withStack: true));
        }

        [Test]
        public void Format_Empty_IsEmpty()
        {
            Assert.AreEqual(string.Empty, HLogConsoleSelection.Format(new List<HLogCore.LogEntry>(), withStack: false));
        }

        private static string Line(HLogCore.LogEntry e)
        {
            string tag = string.IsNullOrEmpty(e.Tag) ? string.Empty : $" [{e.Tag}]";
            return $"{e.TimeString} [{e.LogType}]{tag} {e.Message.Replace('\n', ' ')}";
        }
    }
}
