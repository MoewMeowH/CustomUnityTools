using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>정적 상태 초기화로 id가 다시 발급될 때 이전 세션의 조회를 거절하는지</summary>
    public class CheatSessionTests : CheatTestFixture
    {
        // Play Mode 재시작·재컴파일에 해당: 정적 실행 상태가 초기화된다
        private static void RestartSession() => CheatSystem.InitializeHeadlessForTests();

        [Test]
        public void SessionId_IsStableWithinSession_AndRecordedOnExecution()
        {
            string session = CheatSystem.SessionId;
            Assert.AreEqual(8, session.Length);
            var e = CheatSystem.Execute(Def(LocalId), null);
            Assert.AreEqual(session, e.Session);
            Assert.AreEqual(session, CheatSystem.SessionId);
        }

        [Test]
        public void Json_HasSession()
        {
            string session = CheatSystem.SessionId;
            StringAssert.StartsWith($"{{\"ok\":true,\"session\":\"{session}\",", CheatProtocol.Run(LocalId, null));
            StringAssert.StartsWith($"{{\"ok\":true,\"session\":\"{session}\",", CheatProtocol.History(20));
        }

        [Test]
        public void Status_SameSession_Ok_OmittedSession_LegacyBehavior()
        {
            var e = CheatSystem.Execute(Def(LocalId), null);
            StringAssert.Contains("\"status\":\"Succeeded\"", CheatProtocol.Status(e.Id, e.Session));
            StringAssert.Contains("\"status\":\"Succeeded\"", CheatProtocol.Status(e.Id));
            StringAssert.Contains("\"status\":\"Succeeded\"", CheatProtocol.Status(e.Id, ""));
        }

        [Test]
        public void Status_AfterRestart_SameIdReissued_IsSessionChanged()
        {
            var old = CheatSystem.Execute(Def(LocalId), null);
            RestartSession();
            var reissued = CheatSystem.Execute(Def(LocalId), null);
            Assert.AreEqual(old.Id, reissued.Id, "전제: 같은 id가 다시 발급된다");
            Assert.AreNotEqual(old.Session, reissued.Session);

            string json = CheatProtocol.Status(old.Id, old.Session);
            StringAssert.StartsWith("{\"ok\":false,", json);
            StringAssert.Contains("\"code\":\"session_changed\"", json);
            // 세션을 넘기지 않으면 다른 실행을 돌려준다 (그래서 자동화는 항상 넘겨야 한다)
            StringAssert.Contains("\"ok\":true", CheatProtocol.Status(old.Id));
        }

        [Test]
        public void Status_AfterRestart_NoExecutionsYet_IsSessionChanged()
        {
            var old = CheatSystem.Execute(Def(LocalId), null);
            RestartSession();   // 재시작 직후, 실행 0건

            string json = CheatProtocol.Status(old.Id, old.Session);
            StringAssert.Contains("\"code\":\"session_changed\"", json);
            StringAssert.Contains($"({old.Session} -> {CheatSystem.SessionId})", json);
        }

        [Test]
        public void Handle_StatusWithSession()
        {
            var e = CheatSystem.Execute(Def(LocalId), null);
            StringAssert.Contains("\"ok\":true", CheatProtocol.Handle($"status {e.Id} session={e.Session}"));
            StringAssert.Contains("\"code\":\"session_changed\"", CheatProtocol.Handle($"status {e.Id} session=deadbeef"));
        }

        [Test]
        public void NotInitialized_HasCode()
        {
            CheatSystem.ResetForTests();
            StringAssert.Contains("\"code\":\"not_initialized\"", CheatProtocol.Run(LocalId, null));
            StringAssert.Contains("\"code\":\"not_initialized\"", CheatProtocol.Raw("x"));
        }
    }
}
