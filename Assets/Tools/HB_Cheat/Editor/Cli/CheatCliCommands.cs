using System;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// Unity CLI(unity command)용 치트 명령. 에디터 Play Mode 대상. 결과는 CheatProtocol과 같은 JSON.
    ///   unity command cheat_list
    ///   unity command cheat_options --id item.enchant --arg level --args "itemId=1001"
    ///   unity command cheat_run --id item.enchant --args "itemId=1001 level=15"
    ///   unity command cheat_status --execution 3
    /// 서버 치트는 Pending으로 시작하므로 cheat_status로 결과를 확인한다.
    /// 실패는 예외가 아니라 항상 {"ok":false,"error":"..."} JSON으로 돌려준다.
    /// </summary>
    internal static class CheatCliCommands
    {
        [CliCommand("cheat_list", "List registered HB_Cheat cheats with their arguments (JSON)")]
        public static string List() => Guard(CheatProtocol.List);

        [CliCommand("cheat_options", "List selectable values of a cheat argument (JSON)")]
        public static string Options(
            [CliArg("id", "Cheat id", Required = true)] string id,
            [CliArg("arg", "Argument name", Required = true)] string arg,
            [CliArg("args", "Earlier argument values for dependent lists, e.g. \"itemId=1001\"")] string args = "",
            [CliArg("query", "Filter by label or value")] string query = "",
            [CliArg("limit", "Maximum number of options (default 200)")] int limit = 200)
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error)) return CheatProtocol.Error(error);
                return CheatProtocol.Options(id, arg, pairs, query, limit);
            });
        }

        [CliCommand("cheat_run", "Run a cheat in Play Mode. Server cheats start as Pending; poll cheat_status (JSON)")]
        public static string Run(
            [CliArg("id", "Cheat id", Required = true)] string id,
            [CliArg("args", "Argument values, e.g. \"itemId=1001 level=15\" (quote values with spaces)")] string args = "")
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error)) return CheatProtocol.Error(error);
                return CheatProtocol.Run(id, pairs);
            });
        }

        [CliCommand("cheat_raw", "Send a raw command string to the server in Play Mode (JSON)")]
        public static string Raw([CliArg("command", "Command string", Required = true)] string command)
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.Raw(command);
            });
        }

        [CliCommand("cheat_status", "Get the status of a cheat execution (JSON)")]
        public static string Status([CliArg("execution", "Execution id returned by cheat_run", Required = true)] int execution)
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.Status(execution);
            });
        }

        [CliCommand("cheat_history", "Recent cheat executions (JSON)")]
        public static string History([CliArg("limit", "Number of entries (default 20)")] int limit = 20)
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.History(limit);
            });
        }

        // 예외가 CLI 프레임워크로 나가면 JSON 대신 400 Bad Request가 된다 → 자동화가 결과를 읽을 수 없다
        private static string Guard(Func<string> command)
        {
            try
            {
                return command();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return CheatProtocol.Error(e.Message);
            }
        }

        private static bool TryRequirePlayMode(out string error)
        {
            if (EditorApplication.isPlaying && CheatSystem.IsInitialized)
            {
                error = null;
                return true;
            }
            error = CheatProtocol.Error("Enter Play Mode first (CheatSystem is not initialized)");
            return false;
        }
    }
}
