using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HB.Cheat.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Commands;

namespace HB.Cheat.Tests
{
    /// <summary>CLI 명령(파이프라인 패키지가 있을 때만 컴파일): 응답 나누기 · 오류 code → 종료 코드 대응. 명령 자체는 CommandContext를 만들 수 없어 직접 부르지 않는다.</summary>
    public class CheatCliCommandsTests
    {
        // 새 code를 계약에 넣으면 여기에도 넣어 종료 코드를 정한다
        private static readonly Dictionary<string, ExitCode> Expected = new Dictionary<string, ExitCode>
        {
            { CheatErrorCode.UnknownCheat, ExitCode.NotFound },
            { CheatErrorCode.UnknownArgument, ExitCode.InvalidUsage },
            { CheatErrorCode.UnknownExecution, ExitCode.NotFound },
            { CheatErrorCode.EvictedExecution, ExitCode.NotFound },
            { CheatErrorCode.EmptyCommand, ExitCode.InvalidUsage },
            { CheatErrorCode.UnknownCommand, ExitCode.InvalidUsage },
            { CheatErrorCode.InvalidUsage, ExitCode.InvalidUsage },
            { CheatErrorCode.MalformedArgument, ExitCode.InvalidUsage },
            { CheatErrorCode.ArgumentWithoutOptions, ExitCode.InvalidUsage },
            { CheatErrorCode.UnparsableValue, ExitCode.InvalidUsage },
            { CheatErrorCode.MissingArgument, ExitCode.InvalidUsage },
            { CheatErrorCode.OutOfRangeValue, ExitCode.InvalidUsage },
            { CheatErrorCode.UnlistedValue, ExitCode.InvalidUsage },
            { CheatErrorCode.EmptyRawCommand, ExitCode.InvalidUsage },
            { CheatErrorCode.NotInitialized, ExitCode.Failed },
            { CheatErrorCode.SessionChanged, ExitCode.Failed },
            { CheatErrorCode.RawDisabled, ExitCode.Failed },
            { CheatErrorCode.OptionProviderFailed, ExitCode.Failed },
            { CheatErrorCode.InternalError, ExitCode.Failed },
        };

        [Test]
        public void ExitCodeOf_EveryContractCode_HasDecidedExitCode()
        {
            var contract = typeof(CheatErrorCode).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
            CollectionAssert.AreEquivalent(contract, Expected.Keys, "CheatErrorCode와 이 표가 다름");

            foreach (var pair in Expected)
                Assert.AreEqual(pair.Value, CheatCliCommands.ExitCodeOf(pair.Key), pair.Key);
        }

        [Test]
        public void Classify_Ok_IsSuccess_AndKeepsDateLikeStringAsString()
        {
            const string date = "2026-11-20T10:00:00.000+09:00";
            var exit = CheatCliCommands.Classify("{\"ok\":true,\"value\":\"" + date + "\"}", out var result, out string errorLine);

            Assert.AreEqual(ExitCode.Success, exit);
            Assert.IsNull(errorLine);
            Assert.AreEqual(JTokenType.String, result["value"].Type);
            Assert.AreEqual(date, result.Value<string>("value"));
        }

        [Test]
        public void Classify_Failure_GivesCodeLineAndExitCode()
        {
            string json = CheatProtocol.Error("Unknown cheat 'x'", CheatErrorCode.UnknownCheat);
            var exit = CheatCliCommands.Classify(json, out var result, out string errorLine);

            Assert.AreEqual(ExitCode.NotFound, exit);
            Assert.IsNull(result);
            Assert.AreEqual("[unknown_cheat] Unknown cheat 'x'", errorLine);
        }
    }
}
