using System.Collections.Generic;
using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>Provider 후보는 치트를 고를 때 · 실행할 때 · CLI 조회 때 다시 수집하고, 그 사이(값 검증 · 표시)에는 캐시한다</summary>
    public class CheatOptionCacheTests : CheatTestFixture
    {
        private const string PickId = "hb.test.optioncache";
        private const string OtherId = "hb.test.optioncache.other";

        private sealed class CountingProvider : ICheatOptionProvider
        {
            public int Calls;

            public void Collect(CheatOptionContext context, List<CheatOption> results)
            {
                Calls++;
                results.Add(new CheatOption(1, "One"));
            }
        }

        private CountingProvider m_Provider;
        private CountingProvider m_OtherProvider;
        private CheatDefinition m_Pick;
        private CheatDefinition m_Other;

        [SetUp]
        public void RegisterOptionCheats()
        {
            CheatSystem.InvalidateOptions();
            m_Provider = new CountingProvider();
            m_OtherProvider = new CountingProvider();
            m_Pick = Register(PickId, m_Provider);
            m_Other = Register(OtherId, m_OtherProvider);
        }

        [TearDown]
        public void UnregisterOptionCheats()
        {
            CheatRegistry.Unregister(PickId);
            CheatRegistry.Unregister(OtherId);
            CheatSystem.InvalidateOptions();
        }

        private static CheatDefinition Register(string id, ICheatOptionProvider provider)
        {
            var definition = new CheatDefinition
            {
                Id = id,
                Path = "Test/" + id,
                Target = CheatTarget.Local,
                Args = new[] { new CheatArgDefinition { Name = "pick", ValueType = CheatValueType.Int, Editor = CheatEditorKind.Options, Provider = provider } },
                LocalInvoker = _ => CheatResult.Ok()
            };
            CheatRegistry.Register(definition);
            return definition;
        }

        [Test]
        public void ValidateAndGetOptions_ReuseCache()
        {
            var values = new object[] { 1 };
            CheatSystem.GetOptions(m_Pick, 0, values, out _);
            Assert.IsTrue(CheatSystem.Validate(m_Pick, values, out _));
            Assert.IsTrue(CheatSystem.Validate(m_Pick, values, out _));

            Assert.AreEqual(1, m_Provider.Calls);
        }

        [Test]
        public void Execute_RecollectsOnlyThatCheat()
        {
            CheatSystem.GetOptions(m_Pick, 0, new object[] { 1 }, out _);
            CheatSystem.GetOptions(m_Other, 0, new object[] { 1 }, out _);

            var execution = CheatSystem.Execute(m_Pick, new object[] { 1 });
            Assert.AreEqual(CheatStatus.Succeeded, execution.Status, execution.Message);
            Assert.AreEqual(2, m_Provider.Calls);

            CheatSystem.GetOptions(m_Other, 0, new object[] { 1 }, out _);
            Assert.AreEqual(1, m_OtherProvider.Calls);
        }

        [Test]
        public void ProtocolOptions_Recollects()
        {
            CheatSystem.GetOptions(m_Pick, 0, new object[] { 1 }, out _);
            StringAssert.Contains("\"ok\":true", CheatProtocol.Options(PickId, "pick", null, null, 10));

            Assert.AreEqual(2, m_Provider.Calls);
        }
    }
}
