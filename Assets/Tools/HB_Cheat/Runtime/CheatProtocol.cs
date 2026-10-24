using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HB.Cheat
{
    /// <summary>
    /// 자동화용 텍스트 프로토콜. 한 줄 명령 → JSON 한 줄. Unity CLI 명령, 이후 ADB/소켓 브리지가 같은 처리를 쓴다.
    ///   list
    ///   options &lt;cheatId&gt; &lt;argName&gt; [k=v ...] [query=text] [limit=N]
    ///   run &lt;cheatId&gt; [k=v ...]
    ///   raw &lt;command...&gt;
    ///   status &lt;executionId&gt;
    ///   history [limit=N]
    /// 값에 공백이 있으면 "..."로 감싼다. 결과: {"ok":true,...} 또는 {"ok":false,"error":"..."}
    /// </summary>
    public static class CheatProtocol
    {
        /// <summary>브리지(ICheatBridge)용 진입점. 실행 주체는 Bridge로 기록된다.</summary>
        public static string Handle(string line) => Handle(line, CheatSource.Bridge);

        public static string Handle(string line, CheatSource source)
        {
            try
            {
                var tokens = Tokenize(line);
                if (tokens.Count == 0) return Error("Empty command", CheatErrorCode.EmptyCommand);

                string verb = tokens[0].ToLowerInvariant();
                Dictionary<string, string> pairs;
                string pairError;
                switch (verb)
                {
                    case "list":
                        return List();
                    case "options":
                        if (tokens.Count < 3) return Error("Usage: options <cheatId> <argName> [k=v ...]", CheatErrorCode.InvalidUsage);
                        if (!TryParsePairs(tokens, 3, out pairs, out pairError)) return Error(pairError, CheatErrorCode.MalformedArgument);
                        pairs.TryGetValue("query", out string query);
                        int limit = pairs.TryGetValue("limit", out string limitText) && int.TryParse(limitText, out int l) ? l : 200;
                        return Options(tokens[1], tokens[2], pairs, query, limit);
                    case "run":
                        if (tokens.Count < 2) return Error("Usage: run <cheatId> [k=v ...]", CheatErrorCode.InvalidUsage);
                        if (!TryParsePairs(tokens, 2, out pairs, out pairError)) return Error(pairError, CheatErrorCode.MalformedArgument);
                        return Run(tokens[1], pairs, source);
                    case "raw":
                        if (tokens.Count < 2) return Error("Usage: raw <command...>", CheatErrorCode.InvalidUsage);
                        return Raw(RestAfterFirstToken(line), source);
                    case "status":
                        if (tokens.Count < 2 || !int.TryParse(tokens[1], out int id)) return Error("Usage: status <executionId> [session=S]", CheatErrorCode.InvalidUsage);
                        if (!TryParsePairs(tokens, 2, out pairs, out pairError)) return Error(pairError, CheatErrorCode.MalformedArgument);
                        pairs.TryGetValue("session", out string session);
                        return Status(id, session);
                    case "history":
                        if (!TryParsePairs(tokens, 1, out pairs, out pairError)) return Error(pairError, CheatErrorCode.MalformedArgument);
                        int count = pairs.TryGetValue("limit", out string countText) && int.TryParse(countText, out int c) ? c : 20;
                        return History(count);
                    default:
                        return Error($"Unknown command '{tokens[0]}'", CheatErrorCode.UnknownCommand);
                }
            }
            catch (Exception e)
            {
                return Error(e.Message, CheatErrorCode.InternalError);
            }
        }

        #region Commands

        public static string List()
        {
            var w = new CheatJsonWriter();
            w.BeginObject().Prop("ok", true).Key("cheats").BeginArray();
            foreach (var def in CheatRegistry.All)
            {
                w.BeginObject()
                    .Prop("id", def.Id)
                    .Prop("path", def.Path)
                    .Prop("target", def.Target.ToString())
                    .Prop("description", def.Description)
                    .Prop("favorite", CheatSystem.IsFavorite(def.Id))
                    .Key("args").BeginArray();
                foreach (var arg in def.Args)
                {
                    w.BeginObject()
                        .Prop("name", arg.Name)
                        .Prop("label", arg.DisplayLabel)
                        .Prop("type", arg.ValueType == CheatValueType.Enum && arg.EnumType != null ? arg.EnumType.Name : arg.ValueType.ToString())
                        .Prop("editor", arg.Editor.ToString())
                        .Prop("default", CheatValueParser.ToText(CheatValueParser.DefaultOf(arg)))
                        .Prop("hasOptions", arg.HasOptions);
                    if (arg.HasRange) w.Prop("min", arg.Min).Prop("max", arg.Max);
                    if (arg.DependsOn.Length > 0)
                    {
                        w.Key("dependsOn").BeginArray();
                        foreach (int dep in arg.DependsOn) w.Value(def.Args[dep].Name);
                        w.EndArray();
                    }
                    w.EndObject();
                }
                w.EndArray().EndObject();
            }
            w.EndArray().EndObject();
            return w.ToString();
        }

        public static string Options(string cheatId, string argName, IReadOnlyDictionary<string, string> args, string query, int limit)
        {
            if (!CheatRegistry.TryGet(cheatId, out var def)) return Error($"Unknown cheat '{cheatId}'", CheatErrorCode.UnknownCheat);
            int index = def.IndexOfArg(argName);
            if (index < 0) return Error($"'{cheatId}' has no argument '{argName}'", CheatErrorCode.UnknownArgument);
            if (!def.Args[index].HasOptions) return Error($"'{argName}' has no option list", CheatErrorCode.ArgumentWithoutOptions);

            if (!TryBuildValues(def, args, allowMissing: true, out var values, out string error, out string code)) return Error(error, code);

            // CLI 조회는 치트를 고르는 것과 같다 → 최신 후보를 다시 모은다
            CheatOptionCache.Invalidate(def.Id);
            var options = CheatSystem.GetOptions(def, index, values, out string optionError);
            if (optionError != null) return Error(optionError, CheatErrorCode.OptionProviderFailed);

            var w = new CheatJsonWriter();
            w.BeginObject().Prop("ok", true).Prop("total", options.Count).Key("options").BeginArray();
            int written = 0;
            for (int i = 0; i < options.Count && written < limit; i++)
            {
                var option = options[i];
                string valueText = CheatValueParser.ToText(option.Value);
                if (!string.IsNullOrEmpty(query)
                    && option.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && valueText.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                w.BeginObject().Prop("value", valueText).Prop("label", option.Label).EndObject();
                written++;
            }
            w.EndArray().EndObject();
            return w.ToString();
        }

        public static string Run(string cheatId, IReadOnlyDictionary<string, string> args, CheatSource source = CheatSource.Code)
        {
            if (!CheatSystem.IsInitialized) return NotInitialized();
            if (!CheatRegistry.TryGet(cheatId, out var def)) return Error($"Unknown cheat '{cheatId}'", CheatErrorCode.UnknownCheat);
            if (!TryBuildValues(def, args, allowMissing: false, out var values, out string error, out string code)) return Error(error, code);
            // 인자 문제(범위 밖·목록에 없는 값 등)는 실행을 만들지 않고 ok:false로 돌려준다. 실행 결과 문제만 ok:true + Failed
            if (!CheatSystem.Validate(def, values, out error, out code)) return Error(error, code);

            return ExecutionJson(CheatSystem.Execute(def, values, source));
        }

        /// <summary>자동화 주체(Cli·Bridge)는 CheatRuntimeOptions.AllowRawFromAutomation이 켜져 있어야 보낸다 (기본 꺼짐).</summary>
        public static string Raw(string command, CheatSource source = CheatSource.Code)
        {
            if (IsAutomation(source) && !CheatSystem.Options.AllowRawFromAutomation)
                return Error("Raw commands from automation are disabled (CheatRuntimeOptions.AllowRawFromAutomation, Tools > HB > Cheat)", CheatErrorCode.RawDisabled);
            if (!CheatSystem.IsInitialized) return NotInitialized();
            if (string.IsNullOrWhiteSpace(command)) return Error("Empty raw command", CheatErrorCode.EmptyRawCommand);
            return ExecutionJson(CheatSystem.ExecuteRaw(command, source));
        }

        /// <summary>
        /// 찾지 못하면 code로 구분한다: evicted_execution(현재 세션에서 실행됐지만 히스토리 용량으로 지워짐), unknown_execution(현재 세션에서 발급된 적 없는 id).
        /// 끝나지 않은 실행은 히스토리에서 지워지지 않으므로 둘 다 다시 조회할 필요가 없다.
        /// </summary>
        /// <remarks>
        /// session(run 응답의 "session")을 넘기면 현재 세션과 다를 때 code "session_changed"로 거절한다.
        /// 정적 상태가 초기화되면 id가 1부터 다시 발급되므로, 넘기지 않으면 다른 실행의 결과를 받을 수 있다.
        /// </remarks>
        public static string Status(int executionId, string session = null)
        {
            string current = CheatSystem.SessionId;
            if (!string.IsNullOrEmpty(session) && session != current)
                return Error($"Session changed ({session} -> {current}). Execution {executionId} belongs to a previous session", CheatErrorCode.SessionChanged);

            var execution = CheatSystem.FindExecution(executionId);
            if (execution != null) return ExecutionJson(execution);
            if (CheatSystem.WasIssued(executionId))
                return Error($"Execution {executionId} was removed from history (capacity {CheatSystem.HistoryCapacity})", CheatErrorCode.EvictedExecution);
            return Error($"Unknown execution {executionId}", CheatErrorCode.UnknownExecution);
        }

        public static string History(int limit)
        {
            var history = CheatSystem.History;
            var w = new CheatJsonWriter();
            w.BeginObject().Prop("ok", true).Prop("session", CheatSystem.SessionId);
            WriteServerQueue(w);
            w.Key("history").BeginArray();
            for (int i = Math.Max(0, history.Count - limit); i < history.Count; i++)
                WriteExecution(w, history[i]);
            w.EndArray().EndObject();
            return w.ToString();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// "k=v" 값으로 인자 배열을 만든다. 빠진 인자는 기본값 (allowMissing=false면 기본값이 없는 목록 인자는 에러).
        /// </summary>
        public static bool TryBuildValues(CheatDefinition def, IReadOnlyDictionary<string, string> args, bool allowMissing, out object[] values, out string error)
        {
            return TryBuildValues(def, args, allowMissing, out values, out error, out _);
        }

        /// <summary>실패 이유를 CheatErrorCode로도 돌려준다 (unknown_argument / unparsable_value / missing_argument).</summary>
        public static bool TryBuildValues(CheatDefinition def, IReadOnlyDictionary<string, string> args, bool allowMissing, out object[] values, out string error, out string code)
        {
            values = def.CreateDefaultValues();
            error = null;
            code = null;

            if (args != null)
            {
                foreach (var pair in args)
                {
                    if (pair.Key == "query" || pair.Key == "limit") continue;

                    int index = def.IndexOfArg(pair.Key);
                    if (index < 0)
                    {
                        error = $"'{def.Id}' has no argument '{pair.Key}'";
                        code = CheatErrorCode.UnknownArgument;
                        return false;
                    }
                    if (!CheatValueParser.TryParse(def.Args[index], pair.Value, out values[index], out error))
                    {
                        code = CheatErrorCode.UnparsableValue;
                        return false;
                    }
                }
            }

            if (!allowMissing)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == null)
                    {
                        error = $"Missing argument '{def.Args[i].Name}'";
                        code = CheatErrorCode.MissingArgument;
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>{"ok":true,"serverQueue":{...},"execution":{...}}</summary>
        public static string ExecutionJson(CheatExecution execution)
        {
            var w = new CheatJsonWriter();
            w.BeginObject().Prop("ok", true).Prop("session", execution.Session);
            WriteServerQueue(w);
            w.Key("execution");
            WriteExecution(w, execution);
            w.EndObject();
            return w.ToString();
        }

        /// <summary>
        /// "serverQueue":{"paused":bool,"reason":string|null,"queued":N,"inFlight":id|null}
        /// paused면 늦은 응답 또는 ResumeServerQueue(사람·재접속 코드)가 있어야 대기 건이 진행된다.
        /// </summary>
        private static void WriteServerQueue(CheatJsonWriter w)
        {
            var inFlight = CheatSystem.InFlightServerExecution;
            w.Key("serverQueue").BeginObject()
                .Prop("paused", CheatSystem.IsServerQueuePaused)
                .Prop("reason", CheatSystem.ServerQueuePauseReason)
                .Prop("queued", CheatSystem.QueuedServerCount)
                .Key("inFlight");
            if (inFlight != null) w.Value(inFlight.Id);
            else w.Value((string)null);
            w.EndObject();
        }

        private static void WriteExecution(CheatJsonWriter w, CheatExecution e)
        {
            w.BeginObject()
                .Prop("id", e.Id)
                .Prop("cheatId", e.Definition?.Id)
                .Prop("target", e.IsServer ? "Server" : "Local")
                .Prop("source", e.Source.ToString())
                .Prop("status", e.Status.ToString())
                .Prop("done", e.IsDone)
                // 끝나지 않았는데 큐가 멈춰 스스로는 진행되지 않는다 → 자동화는 폴링을 멈추고 사람에게 넘긴다
                .Prop("blocked", e.IsServer && !e.IsDone && CheatSystem.IsServerQueuePaused)
                .Prop("message", e.Message)
                .Prop("command", e.Command)
                .Prop("describe", e.Describe())
                .EndObject();
        }

        /// <summary>Play Mode가 아니거나 재컴파일 등으로 초기화가 풀렸다 → 자동화는 멈추고 사람에게 넘긴다</summary>
        public static string NotInitialized() => Error("CheatSystem is not initialized (enter Play Mode)", CheatErrorCode.NotInitialized);

        /// <summary>
        /// CLI cheat_play(enterPlay) / cheat_stop 요청 판정. 에디터 상태는 호출부가 넘긴다 (이 어셈블리는 UnityEditor를 참조하지 않는다).
        /// changing: Play Mode 전환 중이거나 컴파일·에셋 갱신 중. 문제가 없으면 null.
        /// </summary>
        public static string PlayModeRequestError(bool enterPlay, bool isPlaying, bool changing)
        {
            if (changing) return Error("Play Mode is changing or the editor is compiling/updating. Retry after it settles", CheatErrorCode.PlayModeUnavailable);
            if (enterPlay && isPlaying) return Error("Already in Play Mode", CheatErrorCode.AlreadyPlaying);
            if (!enterPlay && !isPlaying) return Error("Not in Play Mode", CheatErrorCode.NotPlaying);
            return null;
        }

        /// <summary>
        /// cheat_play 응답. runInBackgroundOverride.active가 false면 reason에 이유 (disabled / already_true / pipeline_driver / pipeline_config_unreadable).
        /// original은 읽지 않았으면 null. pipeline: 설정 파일 판정 (none / disabled / enabled / unreadable)과 읽은 값.
        /// </summary>
        public static string PlayEntering(bool active, bool? original, string reason, string pipelineConfig, bool? pipelineEnableInBuilds, bool? pipelineAutoStart)
        {
            var w = new CheatJsonWriter().BeginObject()
                .Prop("ok", true)
                .Prop("playMode", "entering")
                .Key("runInBackgroundOverride").BeginObject()
                .Prop("active", active);
            NullableProp(w, "original", original);
            w.Prop("reason", reason)
                .Key("pipeline").BeginObject()
                .Prop("config", pipelineConfig);
            NullableProp(w, "enableInBuilds", pipelineEnableInBuilds);
            NullableProp(w, "autoStart", pipelineAutoStart);
            return w.EndObject().EndObject().EndObject().ToString();
        }

        /// <summary>cheat_stop 응답. runInBackground 복원은 에디트 모드에 들어간 뒤라 응답 시점에는 아직 켜져 있을 수 있다.</summary>
        public static string PlayExiting()
        {
            return new CheatJsonWriter().BeginObject().Prop("ok", true).Prop("playMode", "exiting").EndObject().ToString();
        }

        private static void NullableProp(CheatJsonWriter w, string key, bool? value)
        {
            if (value.HasValue) w.Prop(key, value.Value);
            else w.Prop(key, (string)null);
        }

        private static bool IsAutomation(CheatSource source) => source == CheatSource.Cli || source == CheatSource.Bridge;

        /// <summary>code 없는 에러. 패키지 안에서는 쓰지 않는다 (자동화가 분기할 수 있도록 Error(message, code)를 쓴다).</summary>
        public static string Error(string message)
        {
            return new CheatJsonWriter().BeginObject().Prop("ok", false).Prop("error", message).EndObject().ToString();
        }

        /// <summary>자동화가 문구 대신 분기할 수 있도록 code(CheatErrorCode)를 붙인 에러</summary>
        public static string Error(string message, string code)
        {
            return new CheatJsonWriter().BeginObject().Prop("ok", false).Prop("error", message).Prop("code", code).EndObject().ToString();
        }

        /// <summary>공백 구분, "..." 안의 공백은 유지, \" 이스케이프</summary>
        public static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(line)) return tokens;

            var sb = new StringBuilder();
            bool inQuote = false;
            bool hasToken = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && inQuote && i + 1 < line.Length)
                {
                    sb.Append(line[++i]);
                }
                else if (c == '"')
                {
                    inQuote = !inQuote;
                    hasToken = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuote)
                {
                    if (hasToken) tokens.Add(sb.ToString());
                    sb.Clear();
                    hasToken = false;
                }
                else
                {
                    sb.Append(c);
                    hasToken = true;
                }
            }
            if (hasToken) tokens.Add(sb.ToString());
            return tokens;
        }

        /// <summary>첫 토큰(명령어) 뒤의 원문. 첫 토큰은 Tokenize와 같은 규칙으로 끝을 찾는다 ("raw"처럼 따옴표로 감싸도 된다)</summary>
        private static string RestAfterFirstToken(string line)
        {
            int i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;

            bool inQuote = false;
            for (; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && inQuote && i + 1 < line.Length) i++;
                else if (c == '"') inQuote = !inQuote;
                else if (char.IsWhiteSpace(c) && !inQuote) break;
            }
            return line.Substring(i).Trim();
        }

        /// <summary>"k=v k2=v2" 형식 (CLI 인자 하나로 받을 때). 형식이 틀리면 FormatException (예외 없이 받으려면 TryParsePairs)</summary>
        public static Dictionary<string, string> ParsePairs(string text) => ParsePairs(Tokenize(text), 0);

        /// <summary>ParsePairs의 예외 없는 버전. 형식이 틀리면 false와 이유</summary>
        public static bool TryParsePairs(string text, out Dictionary<string, string> pairs, out string error)
        {
            return TryParsePairs(Tokenize(text), 0, out pairs, out error);
        }

        private static Dictionary<string, string> ParsePairs(List<string> tokens, int start)
        {
            if (!TryParsePairs(tokens, start, out var pairs, out string error)) throw new FormatException(error);
            return pairs;
        }

        private static bool TryParsePairs(List<string> tokens, int start, out Dictionary<string, string> pairs, out string error)
        {
            pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = start; i < tokens.Count; i++)
            {
                int eq = tokens[i].IndexOf('=');
                if (eq <= 0)
                {
                    error = $"Expected k=v but got '{tokens[i]}'";
                    pairs = null;
                    return false;
                }
                pairs[tokens[i].Substring(0, eq)] = tokens[i].Substring(eq + 1);
            }
            error = null;
            return true;
        }

        #endregion
    }

    /// <summary>의존성 없는 최소 JSON 작성기</summary>
    public sealed class CheatJsonWriter
    {
        private readonly StringBuilder m_Sb = new StringBuilder(256);
        private bool m_NeedComma;

        public CheatJsonWriter BeginObject() { Separator(); m_Sb.Append('{'); m_NeedComma = false; return this; }
        public CheatJsonWriter EndObject() { m_Sb.Append('}'); m_NeedComma = true; return this; }
        public CheatJsonWriter BeginArray() { Separator(); m_Sb.Append('['); m_NeedComma = false; return this; }
        public CheatJsonWriter EndArray() { m_Sb.Append(']'); m_NeedComma = true; return this; }

        public CheatJsonWriter Key(string key)
        {
            Separator();
            WriteString(key);
            m_Sb.Append(':');
            m_NeedComma = false;
            return this;
        }

        public CheatJsonWriter Value(string value) { Separator(); WriteString(value); m_NeedComma = true; return this; }
        public CheatJsonWriter Value(bool value) { Separator(); m_Sb.Append(value ? "true" : "false"); m_NeedComma = true; return this; }
        /// <summary>NaN · Infinity는 JSON 숫자가 아니므로 null로 쓴다</summary>
        public CheatJsonWriter Value(double value)
        {
            Separator();
            if (double.IsNaN(value) || double.IsInfinity(value)) m_Sb.Append("null");
            else m_Sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            m_NeedComma = true;
            return this;
        }

        public CheatJsonWriter Prop(string key, string value) => Key(key).Value(value);
        public CheatJsonWriter Prop(string key, bool value) => Key(key).Value(value);
        public CheatJsonWriter Prop(string key, double value) => Key(key).Value(value);

        public override string ToString() => m_Sb.ToString();

        private void Separator()
        {
            if (m_NeedComma) m_Sb.Append(',');
        }

        private void WriteString(string value)
        {
            if (value == null)
            {
                m_Sb.Append("null");
                return;
            }

            m_Sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': m_Sb.Append("\\\""); break;
                    case '\\': m_Sb.Append("\\\\"); break;
                    case '\n': m_Sb.Append("\\n"); break;
                    case '\r': m_Sb.Append("\\r"); break;
                    case '\t': m_Sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) m_Sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else m_Sb.Append(c);
                        break;
                }
            }
            m_Sb.Append('"');
        }
    }
}
