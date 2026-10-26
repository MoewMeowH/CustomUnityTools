using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// PBXProject API가 주지 않는 정보(빌드 단계에 든 파일, weak 링크 여부)를 project.pbxproj 텍스트에서 읽습니다 (iOS 모듈 없이 동작).
    /// Unity · Xcode가 쓰는 한 줄 형식("GUID /* 이름 */ = {isa = PBXBuildFile; fileRef = GUID ...; settings = {ATTRIBUTES = (Weak, ); }; };")을 기준으로 합니다.
    /// </summary>
    public static class PbxprojText
    {
        private const string GUID = "[0-9A-Fa-f]{24}";

        /// <summary> 빌드 단계(phaseGuid)의 files 목록에 든 PBXBuildFile GUID </summary>
        public static List<string> GetPhaseBuildFiles(string pbx, string phaseGuid)
        {
            if (string.IsNullOrEmpty(phaseGuid)) return new List<string>();
            var m = Regex.Match(pbx, Regex.Escape(phaseGuid) + @"(?: /\*[^\n]*?\*/)? = \{\s*isa = PBX\w+BuildPhase;.*?files = \((?<files>.*?)\);", RegexOptions.Singleline);
            if (!m.Success) return new List<string>();
            return Regex.Matches(m.Groups["files"].Value, GUID).Cast<Match>().Select(x => x.Value).ToList();
        }

        /// <summary> 빌드 단계에 fileRef가 이 파일 참조인 PBXBuildFile이 있는지 </summary>
        public static bool PhaseContainsFileRef(string pbx, string phaseGuid, string fileRefGuid)
        {
            foreach (var bf in GetPhaseBuildFiles(pbx, phaseGuid))
            {
                string line = BuildFileLine(pbx, bf);
                if (line != null && FileRefOf(line) == fileRefGuid) return true;
            }
            return false;
        }

        /// <summary> 빌드 단계에 이 이름(예: AdSupport.framework, libz.tbd)의 파일이 링크돼 있는지와 weak(Optional) 여부 </summary>
        public static bool TryFindLinked(string pbx, string phaseGuid, string fileName, out bool weak)
        {
            weak = false;
            foreach (var bf in GetPhaseBuildFiles(pbx, phaseGuid))
            {
                string line = BuildFileLine(pbx, bf);
                string fileRef = line != null ? FileRefOf(line) : null;
                if (fileRef == null) continue;
                if (!string.Equals(FileRefName(pbx, fileRef), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                weak = Regex.IsMatch(line, @"ATTRIBUTES\s*=\s*\([^)]*\bWeak\b");
                return true;
            }
            return false;
        }

        private static string BuildFileLine(string pbx, string guid)
        {
            var m = Regex.Match(pbx, "^[ \\t]*" + guid + @"(?: /\*[^\n]*?\*/)? = \{isa = PBXBuildFile;[^\n]*$", RegexOptions.Multiline);
            return m.Success ? m.Value : null;
        }

        private static string FileRefOf(string buildFileLine)
        {
            var m = Regex.Match(buildFileLine, @"fileRef = (" + GUID + ")");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary> 파일 참조의 name(없으면 path의 마지막 조각) </summary>
        private static string FileRefName(string pbx, string guid)
        {
            var m = Regex.Match(pbx, "^[ \\t]*" + guid + @"(?: /\*[^\n]*?\*/)? = \{isa = PBXFileReference;[^\n]*$", RegexOptions.Multiline);
            if (!m.Success) return null;
            string Value(string key)
            {
                var v = Regex.Match(m.Value, @"\b" + key + @" = (?:""(?<v>[^""]*)""|(?<v>[^;\s]+));");
                return v.Success ? v.Groups["v"].Value : null;
            }
            string name = Value("name");
            if (!string.IsNullOrEmpty(name)) return name;
            string path = Value("path");
            return path?.Split('/').Last();
        }
    }
}
