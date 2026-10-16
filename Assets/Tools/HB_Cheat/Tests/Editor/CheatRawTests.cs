using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>자동화 경로의 raw 차단 (AllowRawFromAutomation, 기본 false)</summary>
    public class CheatRawTests : CheatTestFixture
    {
        [Test]
        public void DefaultIsDisabled()
        {
            Assert.IsFalse(new CheatRuntimeOptions().AllowRawFromAutomation);
        }

        [TestCase(CheatSource.Cli)]
        [TestCase(CheatSource.Bridge)]
        public void Automation_Disabled_RejectsWithoutExecution(CheatSource source)
        {
            string json = CheatProtocol.Raw("item_add 1 1", source);
            StringAssert.StartsWith("{\"ok\":false,", json);
            StringAssert.Contains("\"code\":\"raw_disabled\"", json);
            Assert.AreEqual(0, CheatSystem.History.Count, "실행을 만들지 않는다");
            Assert.AreEqual(0, Transport.Sent.Count);
        }

        [Test]
        public void HandleRaw_IsBridge_Rejected()
        {
            StringAssert.Contains("\"code\":\"raw_disabled\"", CheatProtocol.Handle("raw item_add 1 1"));
        }

        [TestCase(CheatSource.Code)]
        [TestCase(CheatSource.Panel)]
        public void NonAutomation_StillSends(CheatSource source)
        {
            StringAssert.Contains("\"ok\":true", CheatProtocol.Raw("item_add 1 1", source));
            CollectionAssert.AreEqual(new[] { "item_add 1 1" }, Transport.Sent);
        }

        [Test]
        public void Automation_Enabled_Sends()
        {
            CheatSystem.Options.AllowRawFromAutomation = true;
            StringAssert.Contains("\"source\":\"Cli\"", CheatProtocol.Raw("item_add 1 1", CheatSource.Cli));
            CollectionAssert.AreEqual(new[] { "item_add 1 1" }, Transport.Sent);
        }
    }
}
