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
    ///   unity command cheat_status --execution 3 --session 1a2b3c4d
    ///   unity command cheat_play / cheat_stop
    /// 서버 치트는 Pending으로 시작하므로 cheat_status로 결과를 확인한다.
    /// cheat_play로 시작한 Play 동안만 runInBackground를 켠다 (CliPlayRunInBackground).
    /// 실패는 예외가 아니라 항상 {"ok":false,"error":"..."} JSON으로 돌려준다.
    /// </summary>
    internal static class CheatCliCommands
    {
        [CliCommand("cheat_list", "List registered HB_Cheat cheats with their arguments in Play Mode (JSON)")]
        public static string List()
        {
            // 에디트 모드의 레지스트리는 비었거나 직전 Play의 목록이라 다른 명령처럼 Play Mode에서만 답한다
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.List();
            });
        }

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
                if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error)) return CheatProtocol.Error(error, CheatErrorCode.MalformedArgument);
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
                if (!CheatProtocol.TryParsePairs(args ?? string.Empty, out var pairs, out error)) return CheatProtocol.Error(error, CheatErrorCode.MalformedArgument);
                return CheatProtocol.Run(id, pairs, CheatSource.Cli);
            });
        }

        [CliCommand("cheat_raw", "Send a raw command string to the server in Play Mode (JSON)")]
        public static string Raw([CliArg("command", "Command string", Required = true)] string command)
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.Raw(command, CheatSource.Cli);
            });
        }

        [CliCommand("cheat_status", "Get the status of a cheat execution (JSON)")]
        public static string Status(
            [CliArg("execution", "Execution id returned by cheat_run", Required = true)] int execution,
            [CliArg("session", "Session returned by cheat_run. If it differs from the current session, fails with code session_changed")] string session = "")
        {
            return Guard(() =>
            {
                if (!TryRequirePlayMode(out string error)) return error;
                return CheatProtocol.Status(execution, session);
            });
        }

        [CliCommand("cheat_play", "Enter Play Mode. Keeps Application.runInBackground on during this Play only and restores it afterwards; settings on disk are not written (JSON)")]
        public static string Play()
        {
            return Guard(() =>
            {
                string error = CheatProtocol.PlayModeRequestError(true, EditorApplication.isPlaying, IsPlayModeChanging());
                if (error != null) return error;

                var decision = CliPlayRunInBackgroundHook.Begin(CheatSettings.Load().RunInBackgroundDuringCliPlay);
                EditorApplication.isPlaying = true;
                var pipeline = decision.Pipeline;
                return CheatProtocol.PlayEntering(decision.Active, decision.Original, decision.Reason,
                    PipelineConfigName(pipeline.State), pipeline.EnableInBuilds, pipeline.AutoStart);
            });
        }

        [CliCommand("cheat_stop", "Exit Play Mode. runInBackground set by cheat_play is restored after returning to Edit Mode (JSON)")]
        public static string Stop()
        {
            return Guard(() =>
            {
                string error = CheatProtocol.PlayModeRequestError(false, EditorApplication.isPlaying, IsPlayModeChanging());
                if (error != null) return error;

                EditorApplication.isPlaying = false;
                return CheatProtocol.PlayExiting();
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
                return CheatProtocol.Error(e.Message, CheatErrorCode.InternalError);
            }
        }

        private static bool IsPlayModeChanging()
        {
            return EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying;
        }

        private static string PipelineConfigName(PipelineRuntimeConfigProbe.State state)
        {
            switch (state)
            {
                case PipelineRuntimeConfigProbe.State.NoFile: return "none";
                case PipelineRuntimeConfigProbe.State.Disabled: return "disabled";
                case PipelineRuntimeConfigProbe.State.DriverEnabled: return "enabled";
                default: return "unreadable";
            }
        }

        private static bool TryRequirePlayMode(out string error)
        {
            if (EditorApplication.isPlaying && CheatSystem.IsInitialized)
            {
                error = null;
                return true;
            }
            error = CheatProtocol.NotInitialized();
            return false;
        }
    }
}
