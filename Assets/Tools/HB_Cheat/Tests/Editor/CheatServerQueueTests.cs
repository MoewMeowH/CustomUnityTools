using NUnit.Framework;
using UnityEngine;

namespace HB.Cheat.Tests
{
    /// <summary>서버 큐 정지 상태가 JSON(serverQueue, blocked)에 드러나는지</summary>
    public class CheatServerQueueTests : CheatTestFixture
    {
        // 시간 초과를 기다리지 않고 SentAt을 앞당긴 뒤 Tick을 직접 부른다 (러너 → Tick 연결은 Play Mode에서 확인)
        private static void ForceTimeout(CheatExecution inFlight)
        {
            inFlight.SentAt = Time.realtimeSinceStartup - 1000f;
            CheatSystem.Tick();
        }

        [Test]
        public void Running_NotBlocked()
        {
            var e = CheatSystem.Execute(Def(ServerId), null);
            string json = CheatProtocol.Status(e.Id);
            StringAssert.Contains("\"serverQueue\":{\"paused\":false,\"reason\":null,\"queued\":0,\"inFlight\":1}", json);
            StringAssert.Contains("\"done\":false,\"blocked\":false", json);
        }

        [Test]
        public void Timeout_PausesQueue_QueuedIsBlocked()
        {
            var first = CheatSystem.Execute(Def(ServerId), null);
            var queued = CheatSystem.Execute(Def(ServerId), null);
            ForceTimeout(first);

            Assert.AreEqual(CheatStatus.TimedOut, first.Status);
            string json = CheatProtocol.Status(queued.Id);
            StringAssert.Contains("\"serverQueue\":{\"paused\":true,\"reason\":\"#1 timed out\",\"queued\":1,\"inFlight\":null}", json);
            StringAssert.Contains("\"status\":\"Pending\",\"done\":false,\"blocked\":true", json);
            // 끝난 실행은 blocked가 아니다
            StringAssert.Contains("\"done\":true,\"blocked\":false", CheatProtocol.Status(first.Id));
        }

        [Test]
        public void NewRunWhilePaused_IsBlockedImmediately()
        {
            ForceTimeout(CheatSystem.Execute(Def(ServerId), null));
            string json = CheatProtocol.Run(ServerId, null, CheatSource.Cli);
            StringAssert.Contains("\"paused\":true", json);
            StringAssert.Contains("\"blocked\":true", json);
            Assert.AreEqual(1, Transport.Sent.Count, "멈춘 동안에는 보내지 않는다");
        }

        [Test]
        public void QueueChangedSubscriber_SeesTimedOutAsDone()
        {
            var first = CheatSystem.Execute(Def(ServerId), null);
            string seen = null;
            void OnQueueChanged() => seen = CheatProtocol.Status(first.Id);
            CheatSystem.ServerQueueChanged += OnQueueChanged;
            try
            {
                ForceTimeout(first);
            }
            finally
            {
                CheatSystem.ServerQueueChanged -= OnQueueChanged;
            }
            StringAssert.Contains("\"status\":\"TimedOut\",\"done\":true,\"blocked\":false", seen);
        }

        [Test]
        public void HistoryJson_HasServerQueue()
        {
            ForceTimeout(CheatSystem.Execute(Def(ServerId), null));
            StringAssert.StartsWith("{\"ok\":true,\"serverQueue\":{\"paused\":true,", CheatProtocol.History(20));
        }

        [Test]
        public void Resume_UnblocksAndSends()
        {
            ForceTimeout(CheatSystem.Execute(Def(ServerId), null));
            var queued = CheatSystem.Execute(Def(ServerId), null);

            CheatSystem.ResumeServerQueue();

            Assert.AreSame(queued, CheatSystem.InFlightServerExecution);
            Assert.AreEqual(2, Transport.Sent.Count);
            StringAssert.Contains("\"blocked\":false", CheatProtocol.Status(queued.Id));
        }

        [Test]
        public void LateResponse_DiscardedAndResumes()
        {
            ForceTimeout(CheatSystem.Execute(Def(ServerId), null));
            var queued = CheatSystem.Execute(Def(ServerId), null);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Discarded a late server response"));
            Assert.IsTrue(CheatSystem.ReportServerResult(true, "late"));

            Assert.IsFalse(CheatSystem.IsServerQueuePaused);
            Assert.AreEqual(CheatStatus.Pending, queued.Status, "늦은 응답이 다음 건 결과로 붙지 않는다");
            Assert.AreSame(queued, CheatSystem.InFlightServerExecution);
        }
    }
}
