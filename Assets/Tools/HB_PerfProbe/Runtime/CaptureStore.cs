using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HB.PerfProbe
{
    /// <summary>
    /// 캡처 폴더 관리: <c>&lt;Root&gt;/&lt;yyyyMMdd_HHmmss_fff&gt;_&lt;사유&gt;/</c>.
    /// 이름이 시각으로 시작하므로 이름 순 = 시각 순이다. 개수 상한을 넘으면 오래된 것부터 지운다.
    /// 지우는 것(Prune · Drop)은 캡처 이름 형식인 Root 바로 아래 폴더뿐이다 — Root를 다른 데이터와 같이 쓰는 폴더로 잡아도 그 데이터는 건드리지 않는다.
    /// </summary>
    internal sealed class CaptureStore
    {
        const string TimeFormat = "yyyyMMdd_HHmmss_fff";

        public string Root { get; }
        public int MaxCaptures { get; }

        public CaptureStore(string root, int maxCaptures)
        {
            Root = root;
            MaxCaptures = Math.Max(1, maxCaptures);
        }

        /// <summary>새 캡처 폴더를 만들고 (이름, 절대 경로)를 돌려준다. 만든 뒤 상한을 맞춘다.</summary>
        public (string name, string path) Create(string reason, DateTime now)
        {
            Directory.CreateDirectory(Root);
            string baseName = now.ToString(TimeFormat, CultureInfo.InvariantCulture) + "_" + reason;
            string name = baseName;
            for (int i = 2; Directory.Exists(Path.Combine(Root, name)); i++) name = baseName + "_" + i;
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            Prune(keep: name);
            return (name, path);
        }

        /// <summary>캡처 폴더가 상한을 넘으면 이름 순으로 오래된 것부터 지운다. keep은 지우지 않는다.</summary>
        internal void Prune(string keep)
        {
            var captures = new List<string>();
            foreach (string dir in Directory.GetDirectories(Root))
                if (IsCaptureName(Path.GetFileName(dir))) captures.Add(dir);
            if (captures.Count <= MaxCaptures) return;
            captures.Sort(StringComparer.Ordinal);
            int excess = captures.Count - MaxCaptures;
            for (int i = 0; i < captures.Count && excess > 0; i++)
            {
                if (Path.GetFileName(captures[i]) == keep) continue;
                Directory.Delete(captures[i], true);
                excess--;
            }
        }

        /// <summary>Root 바로 아래 캡처 폴더 하나를 지운다 (PC가 받아 간 뒤).</summary>
        public bool Drop(string name, out string error)
        {
            if (!ProbeCommand.IsSafeValue(name) || !IsCaptureName(name))
            {
                error = "invalid capture name";
                return false;
            }
            string path = Path.Combine(Root, name);
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.Ordinal) || Path.GetFileName(full) != name)
            {
                error = "invalid capture name";
                return false;
            }
            if (!Directory.Exists(path))
            {
                error = "not found";
                return false;
            }
            Directory.Delete(path, true);
            error = null;
            return true;
        }

        /// <summary><c>yyyyMMdd_HHmmss_fff_</c> 다음에 사유가 오는 이름인가.</summary>
        internal static bool IsCaptureName(string name)
        {
            int prefix = TimeFormat.Length;
            if (name == null || name.Length <= prefix + 1 || name[prefix] != '_') return false;
            for (int i = 0; i < prefix; i++)
            {
                char c = name[i];
                bool expectUnderscore = i == 8 || i == 15;
                if (expectUnderscore ? c != '_' : (c < '0' || c > '9')) return false;
            }
            return true;
        }
    }
}
