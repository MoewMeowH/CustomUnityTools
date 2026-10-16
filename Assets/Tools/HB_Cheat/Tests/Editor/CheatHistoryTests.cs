using System.Linq;
using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>히스토리 용량 정리: 끝나지 않은 실행은 지우지 않는다</summary>
    public class CheatHistoryTests : CheatTestFixture
    {
        private static int[] Ids() => CheatSystem.History.Select(e => e.Id).ToArray();

        [Test]
        public void LocalOnly_KeepsNewestCapacity()
        {
            CheatSystem.Options.HistoryCapacity = 2;
            for (int i = 0; i < 4; i++) CheatSystem.Execute(Def(LocalId), null);
            CollectionAssert.AreEqual(new[] { 3, 4 }, Ids());
        }

        [Test]
        public void PendingServer_IsNeverEvicted()
        {
            CheatSystem.Options.HistoryCapacity = 2;
            var inFlight = CheatSystem.Execute(Def(ServerId), null);   // 응답 없음 → 계속 Pending
            var queued = CheatSystem.Execute(Def(ServerId), null);
            for (int i = 0; i < 3; i++) CheatSystem.Execute(Def(LocalId), null);

            Assert.AreSame(inFlight, CheatSystem.FindExecution(inFlight.Id));
            Assert.AreSame(queued, CheatSystem.FindExecution(queued.Id));
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 5 }, Ids());
            StringAssert.Contains("\"status\":\"Pending\"", CheatProtocol.Status(inFlight.Id));
        }

        [Test]
        public void FinishedResult_SurvivesUntilNewEntriesPushItOut()
        {
            CheatSystem.Options.HistoryCapacity = 2;
            var first = CheatSystem.Execute(Def(ServerId), null);
            CheatSystem.Execute(Def(ServerId), null);
            CheatSystem.Execute(Def(ServerId), null);   // 셋 다 Pending, 용량 2를 넘음

            Assert.IsTrue(CheatSystem.ReportServerResult(true, "ok"));
            Assert.AreEqual(CheatStatus.Succeeded, first.Status);
            // 끝난 순간에는 지우지 않는다 → 결과 조회 가능
            StringAssert.Contains("\"status\":\"Succeeded\"", CheatProtocol.Status(first.Id));

            // 새 실행이 들어오면 범위 밖의 끝난 건부터 빠진다
            CheatSystem.Execute(Def(LocalId), null);
            Assert.IsNull(CheatSystem.FindExecution(first.Id));
        }

        [Test]
        public void NestedLocal_KeepsNewestIssuedNotLastAdded()
        {
            const string outerId = "hb.test.outer";
            CheatRegistry.Register(new CheatDefinition
            {
                Id = outerId,
                Path = "Test/Outer",
                Target = CheatTarget.Local,
                // 안쪽 치트(#2)가 바깥(#1)보다 먼저 히스토리에 들어간다
                LocalInvoker = _ => CheatResult.Ok(CheatSystem.Execute(Def(LocalId), null).Id.ToString())
            });
            try
            {
                CheatSystem.Options.HistoryCapacity = 1;
                CheatSystem.Execute(Def(outerId), null);
                CollectionAssert.AreEqual(new[] { 2 }, Ids());
                StringAssert.Contains("\"status\":\"Succeeded\"", CheatProtocol.Status(2));
            }
            finally
            {
                CheatRegistry.Unregister(outerId);
            }
        }

        [Test]
        public void Status_DistinguishesEvictedAndUnknown()
        {
            CheatSystem.Options.HistoryCapacity = 1;
            CheatSystem.Execute(Def(LocalId), null);
            CheatSystem.Execute(Def(LocalId), null);

            Assert.AreEqual("{\"ok\":false,\"error\":\"Execution 1 was removed from history (capacity 1)\",\"code\":\"evicted\"}", CheatProtocol.Status(1));
            Assert.AreEqual("{\"ok\":false,\"error\":\"Unknown execution 99\",\"code\":\"unknown\"}", CheatProtocol.Status(99));
            StringAssert.Contains("\"code\":\"unknown\"", CheatProtocol.Status(0));
        }
    }
}
