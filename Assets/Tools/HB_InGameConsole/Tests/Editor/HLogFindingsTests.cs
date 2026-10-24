using InGameConsoleSystem.Editor;
using NUnit.Framework;
using UnityEngine;

namespace InGameConsoleSystem.Tests
{
    /// <summary>2026-10-24 코드 읽기 지적: 생성기 문자열 이스케이프 · 클래스 이름 검사, 콜론 태그는 등록된 채널만</summary>
    public class HLogFindingsTests
    {
        private LogChannelGeneratorSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<LogChannelGeneratorSettings>();
            settings.Channels.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void ToStringLiteral_EscapesQuoteBackslashAndNewline()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\nd\"", LogChannelGeneratorWindow.ToStringLiteral("a\"b\\c\nd"));
        }

        [Test]
        public void GenerateCode_TagWithQuote_IsEscaped()
        {
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("Battle") { tagName = "Bat\"tle" });
            string code = LogChannelGeneratorWindow.GenerateCode(settings);
            StringAssert.Contains("HLogCore.GetChannel(\"Bat\\\"tle\")", code);
        }

        [Test]
        public void ValidateChannels_DuplicateAfterSanitize_And_Empty_AreErrors()
        {
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("My-Tag"));
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("MyTag"));
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("!!!"));
            Assert.AreEqual(2, LogChannelGeneratorWindow.ValidateChannels(settings).Count);
        }

        [Test]
        public void ValidateChannels_ReservedNames_AreSkippedNotErrors()
        {
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("Common"));
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("Battle"));
            Assert.AreEqual(0, LogChannelGeneratorWindow.ValidateChannels(settings).Count);
        }

        [Test]
        public void ColonTag_UnregisteredWord_IsNotTag()
        {
            var entry = new HLogCore.LogEntry("NoteUnregistered: hello", string.Empty, LogType.Log);
            Assert.AreEqual(string.Empty, entry.Tag);
            Assert.AreEqual("NoteUnregistered: hello", entry.Message);
        }

        [Test]
        public void ColonTag_RegisteredChannel_IsTag_IncludingNonAscii()
        {
            HLogCore.GetChannel("Test 전투 로그");
            var entry = new HLogCore.LogEntry("Test 전투 로그: hello", string.Empty, LogType.Log);
            Assert.AreEqual("Test 전투 로그", entry.Tag);
            Assert.AreEqual("hello", entry.Message);
        }

        [Test]
        public void ColonTag_ExceptionLog_IsNotTag()
        {
            HLogCore.GetChannel("Test_FindingsExceptionChannel");
            var entry = new HLogCore.LogEntry("Test_FindingsExceptionChannel: boom", string.Empty, LogType.Exception);
            Assert.AreEqual(string.Empty, entry.Tag);
        }

        [Test]
        public void SharpTag_StillParsed()
        {
            var entry = new HLogCore.LogEntry("#Foo# hello", string.Empty, LogType.Log);
            Assert.AreEqual("Foo", entry.Tag);
            Assert.AreEqual("hello", entry.Message);
        }

        [Test]
        public void ChannelTags_ContainsCreatedChannel_NotLogDerivedTag()
        {
            // 테스트 채널은 지울 수 없는 정적 캐시에 남는다(도메인 리로드 때 사라짐) → 이름으로 테스트용임을 표시
            HLogCore.GetChannel("Test_FindingsChannelTags");
            // 로그에서 생긴 태그(#Foo#)는 KnownTags에만 등록된다 (AddEntry와 같은 경로)
            HLogCore.RegisterTag("Test_FindingsLogOnlyTag");
            CollectionAssert.Contains(HLogCore.ChannelTags, "Test_FindingsChannelTags");
            CollectionAssert.DoesNotContain(HLogCore.ChannelTags, "Test_FindingsLogOnlyTag");
        }

        [TestCase("Channel")]
        [TestCase("HLog")]
        [TestCase("class")]
        [TestCase("Watch")]
        public void ValidateChannels_HLogMemberOrKeyword_IsError(string name)
        {
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem(name));
            Assert.AreEqual(1, LogChannelGeneratorWindow.ValidateChannels(settings).Count);
        }

        [Test]
        public void ToStringLiteral_EscapesLineAndParagraphSeparators()
        {
            Assert.AreEqual("\"a\\u2028b\\u2029c\"", LogChannelGeneratorWindow.ToStringLiteral("a\u2028b\u2029c"));
        }

        [Test]
        public void GenerateCode_DefaultChannels_OnlyCommon_AndGetterWatchOnly()
        {
            string code = LogChannelGeneratorWindow.GenerateCode(settings);
            StringAssert.Contains("public static class Common", code);
            StringAssert.DoesNotContain("public static class System", code);
            StringAssert.DoesNotContain("Watch(string key, object value)", code);
            StringAssert.DoesNotContain("Func<object>", code);
            StringAssert.Contains("Watch<T>(string key, Func<T> getter", code);
        }

        [Test]
        public void ValidateChannels_SystemName_IsAllowedNow()
        {
            settings.Channels.Add(new LogChannelGeneratorSettings.ChannelItem("System"));
            Assert.AreEqual(0, LogChannelGeneratorWindow.ValidateChannels(settings).Count);
            StringAssert.Contains("public static class System", LogChannelGeneratorWindow.GenerateCode(settings));
        }

        [TestCase("Common/Player/Hp", "Player/Hp", true)]
        [TestCase("Player/Hp", "Common/Player/Hp", true)]
        [TestCase("common/player/hp", "Common/Player/Hp", true)]
        [TestCase("Common/MaxHp", "Hp", false)]
        [TestCase("Hp", "Hp", true)]
        public void TriggerKeyMatches_SegmentSuffixOnly(string key, string watchKey, bool expected)
        {
            Assert.AreEqual(expected, HLogWatchTriggers.KeyMatches(key, watchKey));
        }
    }
}
