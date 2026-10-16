using NUnit.Framework;

namespace HB.Cheat.Tests
{
    public class CheatSourceTests : CheatTestFixture
    {
        [Test]
        public void Execute_WithoutSource_IsCode()
        {
            Assert.AreEqual(CheatSource.Code, CheatSystem.Execute(Def(LocalId), null).Source);
            Assert.AreEqual(CheatSource.Code, CheatSystem.ExecuteRaw("x").Source);
        }

        [TestCase(CheatSource.Panel)]
        [TestCase(CheatSource.Cli)]
        [TestCase(CheatSource.Bridge)]
        public void Execute_RecordsSource(CheatSource source)
        {
            Assert.AreEqual(source, CheatSystem.Execute(Def(LocalId), null, source).Source);
            Assert.AreEqual(source, CheatSystem.ExecuteRaw("x", source).Source);
        }

        [Test]
        public void Protocol_RunJson_HasSource()
        {
            StringAssert.Contains("\"source\":\"Cli\"", CheatProtocol.Run(LocalId, null, CheatSource.Cli));
            StringAssert.Contains("\"source\":\"Code\"", CheatProtocol.Run(LocalId, null));
        }

        [Test]
        public void Handle_IsBridge()
        {
            StringAssert.Contains("\"source\":\"Bridge\"", CheatProtocol.Handle($"run {LocalId}"));
            StringAssert.Contains("\"source\":\"Bridge\"", CheatProtocol.Handle("history"));
        }
    }
}
