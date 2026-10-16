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

        [TestCase("level", "Expected k=v but got 'level'")]
        [TestCase("=3", "Expected k=v but got '=3'")]
        [TestCase("itemId=1 level", "Expected k=v but got 'level'")]
        public void TryParsePairs_BadFormat_ReturnsErrorWithoutThrowing(string text, string expected)
        {
            Assert.IsFalse(CheatProtocol.TryParsePairs(text, out var pairs, out string error));
            Assert.IsNull(pairs);
            Assert.AreEqual(expected, error);
        }

        [Test]
        public void TryParsePairs_Valid()
        {
            Assert.IsTrue(CheatProtocol.TryParsePairs("a=1 b=\"x y\" c=", out var pairs, out string error));
            Assert.IsNull(error);
            Assert.AreEqual("1", pairs["a"]);
            Assert.AreEqual("x y", pairs["b"]);
            Assert.AreEqual("", pairs["c"]);
        }

        [Test]
        public void Handle_BadPairs_ReturnsJsonError()
        {
            Assert.AreEqual("{\"ok\":false,\"error\":\"Expected k=v but got 'level'\",\"code\":\"malformed_argument\"}", CheatProtocol.Handle($"run {IntArgId} level"));
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
