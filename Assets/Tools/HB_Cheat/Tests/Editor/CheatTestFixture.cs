using System.Collections.Generic;
using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>
    /// 러너 없이 CheatSystem을 켜고, 테스트용 치트를 등록·해제한다.
    /// 시간 초과는 SentAt을 앞당긴 뒤 CheatSystem.Tick을 직접 부른다.
    /// </summary>
    public abstract class CheatTestFixture
    {
        protected const string LocalId = "hb.test.local";
        protected const string ServerId = "hb.test.server";
        protected const string IntArgId = "hb.test.intarg";

        private CheatRuntimeOptions m_SavedOptions;
        protected TestTransport Transport;

        [SetUp]
        public void SetUpCheatSystem()
        {
            m_SavedOptions = CheatSystem.Options;
            CheatSystem.Options = new CheatRuntimeOptions();
            CheatSystem.InitializeHeadlessForTests();
            Transport = new TestTransport();
            CheatSystem.ServerTransport = Transport;

            CheatRegistry.Register(new CheatDefinition
            {
                Id = LocalId,
                Path = "Test/Local",
                Target = CheatTarget.Local,
                LocalInvoker = _ => CheatResult.Ok("done")
            });
            CheatRegistry.Register(new CheatDefinition
            {
                Id = ServerId,
                Path = "Test/Server",
                Target = CheatTarget.Server,
                ServerCommandBuilder = _ => "test_server"
            });
            CheatRegistry.Register(new CheatDefinition
            {
                Id = IntArgId,
                Path = "Test/IntArg",
                Target = CheatTarget.Local,
                Args = new[] { new CheatArgDefinition { Name = "level", ValueType = CheatValueType.Int, Editor = CheatEditorKind.Number, DefaultValue = 1 } },
                LocalInvoker = args => CheatResult.Ok($"level {args.Get<int>(0)}")
            });
        }

        [TearDown]
        public void TearDownCheatSystem()
        {
            CheatRegistry.Unregister(LocalId);
            CheatRegistry.Unregister(ServerId);
            CheatRegistry.Unregister(IntArgId);
            CheatSystem.ResetForTests();
            CheatSystem.Options = m_SavedOptions;
        }

        protected static CheatDefinition Def(string id)
        {
            Assert.IsTrue(CheatRegistry.TryGet(id, out var def), id);
            return def;
        }

        /// <summary>응답하지 않는 전송. 보낸 명령만 모은다.</summary>
        protected sealed class TestTransport : ICheatServerTransport
        {
            public readonly List<string> Sent = new List<string>();

            public bool TrySend(string command, out string error)
            {
                Sent.Add(command);
                error = null;
                return true;
            }
        }
    }
}
