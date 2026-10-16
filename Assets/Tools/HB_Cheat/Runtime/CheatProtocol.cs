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
                if (tokens.Count == 0) return Error("Empty command");

                string verb = tokens[0].ToLowerInvariant();
                switch (verb)
                {
                    case "list":
                        return List();
                    case "options":
                        if (tokens.Count < 3) return Error("Usage: options <cheatId> <argName> [k=v ...]");
                        var optionArgs = ParsePairs(tokens, 3);
                        optionArgs.TryGetValue("query", out string query);
                        int limit = optionArgs.TryGetValue("limit", out string limitText) && int.TryParse(limitText, out int l) ? l : 200;
                        return Options(tokens[1], tokens[2], optionArgs, query, limit);
                    case "run":
                        if (tokens.Count < 2) return Error("Usage: run <cheatId> [k=v ...]");
                        return Run(tokens[1], ParsePairs(tokens, 2), source);
                    case "raw":
                        if (tokens.Count < 2) return Error("Usage: raw <command...>");
                        return Raw(line.Substring(line.IndexOf("raw", StringComparison.OrdinalIgnoreCase) + 3).Trim(), source);
                    case "status":
                        if (tokens.Count < 2 || !int.TryParse(tokens[1], out int id)) return Error("Usage: status <executionId>");
                        return Status(id);
                    case "history":
                        var historyArgs = ParsePairs(tokens, 1);
                        int count = historyArgs.TryGetValue("limit", out string countText) && int.TryParse(countText, out int c) ? c : 20;
                        return History(count);
                    default:
                        return Error($"Unknown command '{tokens[0]}'");
                }
            }
            catch (Exception e)
            {
                return Error(e.Message);
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
            if (!CheatRegistry.TryGet(cheatId, out var def)) return Error($"Unknown cheat '{cheatId}'");
            int index = def.IndexOfArg(argName);
            if (index < 0) return Error($"'{cheatId}' has no argument '{argName}'");
            if (!def.Args[index].HasOptions) return Error($"'{argName}' has no option list");

            if (!TryBuildValues(def, args, allowMissing: true, out var values, out string error)) return Error(error);

            var options = CheatSystem.GetOptions(def, index, values, out string optionError);
            if (optionError != null) return Error(optionError);

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
            if (!CheatSystem.IsInitialized) return Error("CheatSystem is not initialized (enter Play Mode)");
            if (!CheatRegistry.TryGet(cheatId, out var def)) return Error($"Unknown cheat '{cheatId}'");
            if (!TryBuildValues(def, args, allowMissing: false, out var values, out string error)) return Error(error);

            return ExecutionJson(CheatSystem.Execute(def, values, source));
        }

        public static string Raw(string command, CheatSource source = CheatSource.Code)
        {
            if (!CheatSystem.IsInitialized) return Error("CheatSystem is not initialized (enter Play Mode)");
            if (string.IsNullOrWhiteSpace(command)) return Error("Empty raw command");
            return ExecutionJson(CheatSystem.ExecuteRaw(command, source));
        }

        /// <summary>
        /// 찾지 못하면 code로 구분한다: "evicted"(이번 세션에 실행됐지만 히스토리 용량으로 지워짐), "unknown"(발급된 적 없는 id).
        /// 끝나지 않은 실행은 히스토리에서 지워지지 않으므로 둘 다 다시 조회할 필요가 없다.
        /// </summary>
        public static string Status(int executionId)
        {
            var execution = CheatSystem.FindExecution(executionId);
            if (execution != null) return ExecutionJson(execution);
            if (CheatSystem.WasIssued(executionId))
                return Error($"Execution {executionId} was removed from history (capacity {CheatSystem.HistoryCapacity})", "evicted");
            return Error($"Unknown execution {executionId}", "unknown");
        }

        public static string History(int limit)
        {
            var history = CheatSystem.History;
            var w = new CheatJsonWriter();
            w.BeginObject().Prop("ok", true);
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
            values = def.CreateDefaultValues();
            error = null;

            if (args != null)
            {
                foreach (var pair in args)
                {
                    if (pair.Key == "query" || pair.Key == "limit") continue;

                    int index = def.IndexOfArg(pair.Key);
                    if (index < 0)
                    {
                        error = $"'{def.Id}' has no argument '{pair.Key}'";
                        return false;
                    }
                    if (!CheatValueParser.TryParse(def.Args[index], pair.Value, out values[index], out error))
                        return false;
                }
            }

            if (!allowMissing)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == null)
                    {
                        error = $"Missing argument '{def.Args[i].Name}'";
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
            w.BeginObject().Prop("ok", true);
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
                .Prop("blocked", !e.IsDone && CheatSystem.IsServerQueuePaused)
                .Prop("message", e.Message)
                .Prop("command", e.Command)
                .Prop("describe", e.Describe())
                .EndObject();
        }

        public static string Error(string message)
        {
            return new CheatJsonWriter().BeginObject().Prop("ok", false).Prop("error", message).EndObject().ToString();
        }

        /// <summary>자동화가 문구 대신 분기할 수 있도록 code를 붙인 에러</summary>
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
        public CheatJsonWriter Value(double value) { Separator(); m_Sb.Append(value.ToString("R", CultureInfo.InvariantCulture)); m_NeedComma = true; return this; }

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
