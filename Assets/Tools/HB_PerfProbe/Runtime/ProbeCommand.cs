using System;
using System.Collections.Generic;

namespace HB.PerfProbe
{
    /// <summary>
    /// 명령 한 줄: <c>이름 [키=값 ...]</c>. 예: <c>capture reason=leak</c>, <c>mark name=boss_start</c>.
    /// 값은 폴더 이름에도 쓰이므로 영문 · 숫자 · <c>_ - .</c>만 받고, 처음 · 끝 글자는 <c>.</c>일 수 없다
    /// (공백 · 경로 구분자 없음, Windows가 끝의 점을 잘라 다른 경로가 되는 것도 막음).
    /// </summary>
    internal sealed class ProbeCommand
    {
        public const int MaxLineLength = 512;
        public const int MaxValueLength = 64;
        /// <summary>캡처 사유 상한. 폴더 이름은 시각 접두사(20자) + 사유 + 충돌 접미사라 MaxValueLength 안에 들어가야 drop할 수 있다.</summary>
        public const int MaxReasonLength = 40;

        public static readonly string[] Names = { "status", "capture", "census", "snapshot", "mark", "drop" };

        public string Name { get; private set; }
        public Dictionary<string, string> Args { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string key, string fallback = null)
        {
            return Args.TryGetValue(key, out string v) ? v : fallback;
        }

        public static bool TryParse(string line, out ProbeCommand command, out string error)
        {
            command = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                error = "empty command";
                return false;
            }
            if (line.Length > MaxLineLength)
            {
                error = $"line longer than {MaxLineLength}";
                return false;
            }

            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string name = tokens[0].ToLowerInvariant();
            if (Array.IndexOf(Names, name) < 0)
            {
                error = $"unknown command '{tokens[0]}'";
                return false;
            }

            var result = new ProbeCommand { Name = name };
            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1)
                {
                    error = $"expected key=value, got '{token}'";
                    return false;
                }
                string key = token.Substring(0, eq).ToLowerInvariant();
                string value = token.Substring(eq + 1);
                if (!IsSafeValue(key) || !IsSafeValue(value))
                {
                    error = $"'{token}': only A-Z a-z 0-9 _ - . allowed (not first or last '.'), up to {MaxValueLength} chars";
                    return false;
                }
                result.Args[key] = value;
            }

            command = result;
            error = null;
            return true;
        }

        internal static bool IsSafeValue(string s)
        {
            if (s.Length == 0 || s.Length > MaxValueLength) return false;
            if (s[0] == '.' || s[s.Length - 1] == '.') return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }
            return true;
        }
    }
}
