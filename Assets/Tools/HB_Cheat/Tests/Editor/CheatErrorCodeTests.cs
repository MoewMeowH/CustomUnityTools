using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace HB.Cheat.Tests
{
    /// <summary>모든 ok:false에 CheatErrorCode가 붙는지, 각 code가 실제로 나오는지</summary>
    public class CheatErrorCodeTests : CheatTestFixture
    {
        private const string RangeId = "hb.test.range";
        private const string ListId = "hb.test.list";
        private const string BadProviderId = "hb.test.badprovider";
        private const string BrokenId = "hb.test.broken";

        private static readonly Regex s_Code = new Regex("\"code\":\"([a-z_]+)\"");

        private sealed class OneTwoProvider : ICheatOptionProvider
        {
            public int Version => 0;
            public void Collect(CheatOptionContext context, List<CheatOption> results)
            {
                results.Add(new CheatOption(1, "One"));
                results.Add(new CheatOption(2, "Two"));
            }
        }

        private sealed class ThrowingProvider : ICheatOptionProvider
        {
            public int Version => 0;
            public void Collect(CheatOptionContext context, List<CheatOption> results) => throw new InvalidOperationException("boom");
        }

        [SetUp]
        public void RegisterCodeCheats()
        {
            CheatRegistry.Register(new CheatDefinition
            {
                Id = RangeId,
                Path = "Test/Range",
                Target = CheatTarget.Local,
                Args = new[] { new CheatArgDefinition { Name = "n", ValueType = CheatValueType.Int, Editor = CheatEditorKind.Number, HasRange = true, Min = 1, Max = 10, DefaultValue = 5 } },
                LocalInvoker = _ => CheatResult.Ok()
            });
            CheatRegistry.Register(new CheatDefinition
            {
                Id = ListId,
                Path = "Test/List",
                Target = CheatTarget.Local,
                // 기본값 없는 Provider 목록 인자 → 고를 때까지 비어 있으므로 빼면 missing_argument (FixedOptions는 기본값이 생긴다)
                Args = new[] { new CheatArgDefinition { Name = "pick", ValueType = CheatValueType.Int, Editor = CheatEditorKind.Options, Provider = new OneTwoProvider() } },
                LocalInvoker = _ => CheatResult.Ok()
            });
            CheatRegistry.Register(new CheatDefinition
            {
                Id = BadProviderId,
                Path = "Test/BadProvider",
                Target = CheatTarget.Local,
                Args = new[] { new CheatArgDefinition { Name = "item", ValueType = CheatValueType.Int, Editor = CheatEditorKind.Options, Provider = new ThrowingProvider(), DefaultValue = 1 } },
                LocalInvoker = _ => CheatResult.Ok()
            });
        }

        [TearDown]
        public void UnregisterCodeCheats()
        {
            CheatRegistry.Unregister(RangeId);
            CheatRegistry.Unregister(ListId);
            CheatRegistry.Unregister(BadProviderId);
            CheatRegistry.Unregister(BrokenId);
            CheatOptionCacheReset();
        }

        private static void CheatOptionCacheReset() => CheatSystem.InvalidateOptions();

        private static string CodeOf(string json)
        {
            var match = s_Code.Match(json);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void AssertCode(string expected, string json)
        {
            StringAssert.StartsWith("{\"ok\":false,", json);
            Assert.AreEqual(expected, CodeOf(json), json);
        }

        private static HashSet<string> AllContractCodes()
        {
            return new HashSet<string>(typeof(CheatErrorCode)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()));
        }

        // 각 code를 내는 입력. (기대 code, 호출)
        private IEnumerable<(string Code, Func<string> Call)> FailingCalls()
        {
            yield return (CheatErrorCode.EmptyCommand, () => CheatProtocol.Handle("   "));
            yield return (CheatErrorCode.UnknownCommand, () => CheatProtocol.Handle("jump"));
            yield return (CheatErrorCode.InvalidUsage, () => CheatProtocol.Handle("run"));
            yield return (CheatErrorCode.InvalidUsage, () => CheatProtocol.Handle($"options {ListId}"));
            yield return (CheatErrorCode.InvalidUsage, () => CheatProtocol.Handle("raw"));
            yield return (CheatErrorCode.InvalidUsage, () => CheatProtocol.Handle("status abc"));
            yield return (CheatErrorCode.MalformedArgument, () => CheatProtocol.Handle($"run {RangeId} n"));
            yield return (CheatErrorCode.MalformedArgument, () => CheatProtocol.Handle($"options {ListId} pick x"));
            yield return (CheatErrorCode.MalformedArgument, () => CheatProtocol.Handle("status 1 session"));
            yield return (CheatErrorCode.MalformedArgument, () => CheatProtocol.Handle("history 5"));
            yield return (CheatErrorCode.UnknownCheat, () => CheatProtocol.Run("no.such", null));
            yield return (CheatErrorCode.UnknownCheat, () => CheatProtocol.Options("no.such", "a", null, null, 10));
            yield return (CheatErrorCode.UnknownArgument, () => CheatProtocol.Run(RangeId, CheatProtocol.ParsePairs("m=1")));
            yield return (CheatErrorCode.UnknownArgument, () => CheatProtocol.Options(ListId, "nope", null, null, 10));
            yield return (CheatErrorCode.ArgumentWithoutOptions, () => CheatProtocol.Options(RangeId, "n", null, null, 10));
            yield return (CheatErrorCode.UnparsableValue, () => CheatProtocol.Run(RangeId, CheatProtocol.ParsePairs("n=abc")));
            yield return (CheatErrorCode.MissingArgument, () => CheatProtocol.Run(ListId, null));
            yield return (CheatErrorCode.OutOfRangeValue, () => CheatProtocol.Run(RangeId, CheatProtocol.ParsePairs("n=11")));
            yield return (CheatErrorCode.UnlistedValue, () => CheatProtocol.Run(ListId, CheatProtocol.ParsePairs("pick=3")));
            yield return (CheatErrorCode.OptionProviderFailed, () => CheatProtocol.Options(BadProviderId, "item", null, null, 10));
            yield return (CheatErrorCode.OptionProviderFailed, () => CheatProtocol.Run(BadProviderId, null));
            yield return (CheatErrorCode.EmptyRawCommand, () => { CheatSystem.Options.AllowRawFromAutomation = true; return CheatProtocol.Raw("  ", CheatSource.Cli); });
            yield return (CheatErrorCode.RawDisabled, () => { CheatSystem.Options.AllowRawFromAutomation = false; return CheatProtocol.Raw("x", CheatSource.Cli); });
            yield return (CheatErrorCode.UnknownExecution, () => CheatProtocol.Status(999));
            yield return (CheatErrorCode.EvictedExecution, () =>
            {
                CheatSystem.Options.HistoryCapacity = 1;
                CheatSystem.Execute(Def(LocalId), null);
                CheatSystem.Execute(Def(LocalId), null);
                return CheatProtocol.Status(1);
            });
            yield return (CheatErrorCode.SessionChanged, () => CheatProtocol.Status(1, "deadbeef"));
            yield return (CheatErrorCode.InternalError, () =>
            {
                // 깨진 정의(Args null)로 예상하지 못한 예외를 만든다 → Handle 보호막
                CheatRegistry.Register(new CheatDefinition { Id = BrokenId, Path = "Test/Broken", Target = CheatTarget.Local, Args = null });
                return CheatProtocol.Handle("list");
            });
            // 마지막: 초기화 해제
            yield return (CheatErrorCode.NotInitialized, () => { CheatSystem.ResetForTests(); return CheatProtocol.Run(LocalId, null); });
        }

        [Test]
        public void EachFailure_HasExpectedCode()
        {
            // Provider 예외는 의도된 실패라 콘솔 예외 로그를 실패로 보지 않는다
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            foreach (var (code, call) in FailingCalls())
                AssertCode(code, call());
        }

        [Test]
        public void EveryContractCode_IsProducedByTests_AndNoOtherCode()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var contract = AllContractCodes();
            var produced = new HashSet<string>();
            foreach (var (_, call) in FailingCalls())
            {
                string json = call();
                string code = CodeOf(json);
                Assert.IsNotNull(code, "ok:false에 code가 없음: " + json);
                Assert.IsTrue(contract.Contains(code), "CheatErrorCode에 없는 code: " + code);
                produced.Add(code);
            }
            CollectionAssert.AreEquivalent(contract, produced, "테스트로 나오지 않는 계약 code가 있음");
            Assert.AreEqual(19, contract.Count, "계약 code 수가 바뀌었다 (README 표와 함께 갱신)");
        }

        [Test]
        public void ArgumentProblems_DoNotCreateExecution_ResultProblemsDo()
        {
            CheatProtocol.Run(RangeId, CheatProtocol.ParsePairs("n=11"));
            CheatProtocol.Run(ListId, CheatProtocol.ParsePairs("pick=3"));
            Assert.AreEqual(0, CheatSystem.History.Count, "인자 문제는 실행을 만들지 않는다");

            // 실행 결과 문제는 ok:true + Failed
            CheatRegistry.Register(new CheatDefinition { Id = BrokenId, Path = "Test/Fail", Target = CheatTarget.Local, LocalInvoker = _ => CheatResult.Fail("nope") });
            string json = CheatProtocol.Run(BrokenId, null);
            StringAssert.StartsWith("{\"ok\":true,", json);
            StringAssert.Contains("\"status\":\"Failed\"", json);
        }

        [Test]
        public void Panel_StillRecordsArgumentFailures()
        {
            // 패널(Execute 직접 호출)은 기존대로 실행을 만들고 Failed로 남긴다
            var e = CheatSystem.Execute(Def(RangeId), new object[] { 11 }, CheatSource.Panel);
            Assert.AreEqual(CheatStatus.Failed, e.Status);
            Assert.AreSame(e, CheatSystem.FindExecution(e.Id));
        }
    }
}
