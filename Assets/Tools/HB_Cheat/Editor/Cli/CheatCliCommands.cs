using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// Unity CLI(unity command)용 치트 명령. 에디터 Play Mode 대상. 규칙: https://github.com/MoewMeowH/CustomUnityTools/blob/main/CLI_COMMANDS.md
    ///   unity command cheat list
    ///   unity command cheat options --id item.enchant --arg level --args "itemId=1001"
    ///   unity command cheat run --id item.enchant --args "itemId=1001 level=15"
    ///   unity command cheat status --execution 3 --session 1a2b3c4d
    /// 서버 치트는 Pending으로 시작하므로 cheat status로 결과를 확인한다.
    /// Play 시작 · 종료는 Pipeline 기본 명령 editor.playmode start / stop.
    /// 성공은 CheatProtocol과 같은 JSON 객체, 실패는 종료 코드 + "[code] 문구" 오류 한 줄 (MCP 클라이언트는 오류 문구만 받는다).
    /// run · raw는 테스트 계정 전용이라 Additive로 둔다 (치트는 라이브 빌드에 들어가지 않음).
    /// </summary>
    internal static class CheatCliCommands
    {
        private const string Group = "cheat";

        [CliGroupCommand(Group, "List registered HB_Cheat cheats with their arguments (Play Mode)", Effect = CommandEffect.ReadOnly)]
        internal static ExitCode List(CommandContext cmd)
        {
            // 에디트 모드의 레지스트리는 비었거나 직전 Play의 목록이라 다른 명령처럼 Play Mode에서만 답한다
            return Reply(cmd, PlayModeError() ?? CheatProtocol.List());
        }

        [CliGroupCommand(Group, "List selectable values of a cheat argument (Play Mode)", Effect = CommandEffect.ReadOnly)]
        internal static ExitCode Options(CommandContext cmd,
            [CliArg("id", "Cheat id", Required = true)] string id,
            [CliArg("arg", "Argument name", Required = true)] string arg,
            [CliArg("args", "Earlier argument values for dependent lists, e.g. \"itemId=1001\"")] string args = "",
            [CliArg("query", "Filter by label or value")] string query = "",
            [CliArg("limit", "Maximum number of options (default 200)")] int limit = 200)
        {
            string error = PlayModeError();
            if (error != null) return Reply(cmd, error);
            if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error))
                return Reply(cmd, CheatProtocol.Error(error, CheatErrorCode.MalformedArgument));
            return Reply(cmd, CheatProtocol.Options(id, arg, pairs, query, limit));
        }

        [CliGroupCommand(Group, "Run a cheat (Play Mode). Server cheats start as Pending; poll cheat status", Effect = CommandEffect.Additive)]
        internal static ExitCode Run(CommandContext cmd,
            [CliArg("id", "Cheat id", Required = true)] string id,
            [CliArg("args", "Argument values, e.g. \"itemId=1001 level=15\" (quote values with spaces)")] string args = "")
        {
            string error = PlayModeError();
            if (error != null) return Reply(cmd, error);
            if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error))
                return Reply(cmd, CheatProtocol.Error(error, CheatErrorCode.MalformedArgument));
            return Reply(cmd, CheatProtocol.Run(id, pairs, CheatSource.Cli));
        }

        [CliGroupCommand(Group, "Send a raw command string to the server (Play Mode, off by default)", Effect = CommandEffect.Additive)]
        internal static ExitCode Raw(CommandContext cmd,
            [CliArg("command", "Command string", Required = true)] string command)
        {
            return Reply(cmd, PlayModeError() ?? CheatProtocol.Raw(command, CheatSource.Cli));
        }

        [CliGroupCommand(Group, "Get the status of a cheat execution (Play Mode)", Effect = CommandEffect.ReadOnly)]
        internal static ExitCode Status(CommandContext cmd,
            [CliArg("execution", "Execution id returned by cheat run", Required = true)] int execution,
            [CliArg("session", "Session returned by cheat run. If it differs from the current session, fails with session_changed")] string session = "")
        {
            return Reply(cmd, PlayModeError() ?? CheatProtocol.Status(execution, session));
        }

        [CliGroupCommand(Group, "Recent cheat executions (Play Mode)", Effect = CommandEffect.ReadOnly)]
        internal static ExitCode History(CommandContext cmd,
            [CliArg("limit", "Number of entries (default 20)")] int limit = 20)
        {
            return Reply(cmd, PlayModeError() ?? CheatProtocol.History(limit));
        }

        private static ExitCode Reply(CommandContext cmd, string json)
        {
            var exit = Classify(json, out var result, out string errorLine);
            if (exit == ExitCode.Success) cmd.Result.Write(result);
            else cmd.Log.Error(errorLine);
            return exit;
        }

        /// <summary>
        /// CheatProtocol 응답 JSON을 나눈다: ok면 Success와 결과 객체, 아니면 종료 코드와 "[code] 문구".
        /// 날짜 모양 문자열(옵션 값 등)을 Date로 바꾸지 않도록 DateParseHandling.None으로 읽는다 (값을 --args로 되돌려 넣는다).
        /// </summary>
        internal static ExitCode Classify(string json, out JObject result, out string errorLine)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                result = JObject.Load(reader);
            }
            if (result.Value<bool>("ok"))
            {
                errorLine = null;
                return ExitCode.Success;
            }

            string code = result.Value<string>("code");
            errorLine = $"[{code}] {result.Value<string>("error")}";
            result = null;
            return ExitCodeOf(code);
        }

        /// <summary>CheatErrorCode → 종료 코드. 대상(치트 · 실행 id)이 없음 → NotFound, 인자 · 요청 형식 → InvalidUsage, 그 밖(상태 · 설정 · 예외) → Failed.</summary>
        internal static ExitCode ExitCodeOf(string code)
        {
            switch (code)
            {
                case CheatErrorCode.UnknownCheat:
                case CheatErrorCode.UnknownExecution:
                case CheatErrorCode.EvictedExecution:
                    return ExitCode.NotFound;
                case CheatErrorCode.UnknownArgument:
                case CheatErrorCode.EmptyCommand:
                case CheatErrorCode.UnknownCommand:
                case CheatErrorCode.InvalidUsage:
                case CheatErrorCode.MalformedArgument:
                case CheatErrorCode.ArgumentWithoutOptions:
                case CheatErrorCode.UnparsableValue:
                case CheatErrorCode.MissingArgument:
                case CheatErrorCode.OutOfRangeValue:
                case CheatErrorCode.UnlistedValue:
                case CheatErrorCode.EmptyRawCommand:
                    return ExitCode.InvalidUsage;
                default:
                    return ExitCode.Failed;
            }
        }

        private static string PlayModeError()
        {
            return EditorApplication.isPlaying && CheatSystem.IsInitialized ? null : CheatProtocol.NotInitialized();
        }
    }
}
