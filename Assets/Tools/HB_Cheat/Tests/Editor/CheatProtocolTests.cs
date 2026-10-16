using NUnit.Framework;

namespace HB.Cheat.Tests
{
    public class CheatProtocolTests : CheatTestFixture
    {
        [Test]
        public void Tokenize_KeepsQuotedSpaces()
        {
            var tokens = CheatProtocol.Tokenize("run id name=\"a b\" x=1");
            CollectionAssert.AreEqual(new[] { "run", "id", "name=a b", "x=1" }, tokens);
        }

        [Test]
        public void Error_IsOkFalseJson()
        {
            Assert.AreEqual("{\"ok\":false,\"error\":\"a \\\"b\\\"\"}", CheatProtocol.Error("a \"b\""));
        }

        [Test]
        public void Run_Local_Succeeds()
        {
            string json = CheatProtocol.Run(IntArgId, CheatProtocol.ParsePairs("level=3"));
            StringAssert.Contains("\"status\":\"Succeeded\"", json);
            StringAssert.Contains("\"message\":\"level 3\"", json);
        }
    }
}
