using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 산출물 이름에 넣을 소스 정보: 커밋 짧은 해시(커밋 안 한 변경이 있으면 -dirty, 확인하지 못하면 -unknown, git이 없으면 nogit)와 Jenkins 빌드 번호.
    /// 변경 여부는 Unity 프로젝트 폴더만 보고(저장소가 더 넓어도 그 밖은 보지 않음), 빌드 산출물 폴더 · 빌드 로그는 뺀다.
    /// 이름의 표시는 추적용이지 진본 보장이 아니다 (환경 변수 · 출력 경로로 바꿀 수 있음).
    /// </summary>
    internal static class BuildRevision
    {
        public const string NO_GIT = "nogit";
        public const string DIRTY_SUFFIX = "-dirty";
        public const string UNKNOWN_SUFFIX = "-unknown";

        // 산출물 폴더가 이 폴더들(또는 프로젝트 루트)과 같거나 그 위면 소스까지 덮으므로 제외 목록에서 뺀다
        private static readonly string[] s_SourceFolders = { "Assets", "Packages", "ProjectSettings" };

        /// <summary>
        /// 커밋 해시 · 변경 표시. git status를 확인하지 못하면 warning에 이유를 담는다 (호출부가 경고 파일에 올림).
        /// </summary>
        public static string Detect(IEnumerable<string> excludeDirs, IEnumerable<string> excludeFiles, out string warning)
        {
            warning = null;
            string projectRoot = BuildHelper.ResolveProjectRelativePath(".");
            string repoRoot = GitRunner.Run(projectRoot, "rev-parse --show-toplevel", out string error)?.Trim();
            string hash = string.IsNullOrEmpty(repoRoot) ? null : GitRunner.Run(projectRoot, "rev-parse --short=7 HEAD", out error)?.Trim();
            if (string.IsNullOrEmpty(hash))
            {
                // PATH에 git 없음 · 저장소 아님 · 커밋 없음 · safe.directory 거부 등. 원인을 찾을 수 있게 남긴다
                BuildHelper.Log($"[Revision] No git commit for the build name ({NO_GIT}): {error}");
                return NO_GIT;
            }

            // 경로는 저장소 루트 기준으로 나온다. "-- ." 로 프로젝트 폴더만 본다
            string status = GitRunner.Run(projectRoot, "status --porcelain=v1 -z --untracked-files=all -- .", out error);
            if (status == null)
            {
                warning = $"[Revision] git status failed, so uncommitted changes could not be checked. The build name is marked {UNKNOWN_SUFFIX}: {error}";
                return hash + UNKNOWN_SUFFIX;
            }

            var exclusion = new PathExclusion(excludeDirs, excludeFiles);
            string[] fields = status.Split('\0');
            for (int i = 0; i < fields.Length; i++)
            {
                string entry = fields[i];
                if (entry.Length < 4) continue;
                // R(이름 변경) · C(복사)는 다음 필드에 원래 경로가 온다
                if (entry[0] == 'R' || entry[0] == 'C')
                    i++;
                if (!exclusion.IsExcluded(Path.Combine(repoRoot, entry.Substring(3))))
                    return hash + DIRTY_SUFFIX;
            }
            return hash;
        }

        /// <summary>
        /// Jenkins 빌드면 BUILD_NUMBER, 아니면 null. BUILD_NUMBER(숫자)가 있고 Jenkins가 빌드에 넣는 변수
        /// (JENKINS_HOME · JENKINS_URL · BUILD_TAG "jenkins-…") 중 하나가 있어야 한다.
        /// JENKINS_URL은 시스템 설정에 Jenkins URL이 있을 때만 생기고, 개발자 PC에 CLI용으로 넣어 두기도 해서 혼자로는 판정하지 않는다.
        /// </summary>
        public static string DetectJenkinsBuildNumber()
        {
            string number = Environment.GetEnvironmentVariable("BUILD_NUMBER");
            if (string.IsNullOrEmpty(number) || !number.All(char.IsDigit))
                return null;

            bool jenkins = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_HOME"))
                           || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL"))
                           || (Environment.GetEnvironmentVariable("BUILD_TAG") ?? string.Empty).StartsWith("jenkins-", StringComparison.Ordinal);
            return jenkins ? number : null;
        }

        /// <summary>
        /// 산출물 폴더 중 소스를 덮는 것(프로젝트 루트 · Assets · Packages · ProjectSettings와 같거나 그 위)을 뺀 목록. 뺀 것은 covering에.
        /// 그대로 두면 그 아래 변경이 모두 "산출물"로 빠져 -dirty · 바뀐 파일 보고가 꺼진다.
        /// </summary>
        public static List<string> FilterOutputDirs(IEnumerable<string> outputDirs, out List<string> covering)
        {
            string projectRoot = WithSeparator(BuildHelper.ResolveProjectRelativePath("."));
            var sources = new List<string> { projectRoot };
            sources.AddRange(s_SourceFolders.Select(f => WithSeparator(Path.Combine(projectRoot, f))));

            var safe = new List<string>();
            covering = new List<string>();
            foreach (string dir in outputDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                string full = WithSeparator(dir);
                if (sources.Any(src => src.StartsWith(full, StringComparison.OrdinalIgnoreCase)))
                    covering.Add(dir);
                else
                    safe.Add(dir);
            }
            return safe;
        }

        internal static string WithSeparator(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }

    /// <summary> 빌드 산출물 폴더 · 빌드 로그처럼 "소스 변경"으로 보지 않을 경로 </summary>
    internal sealed class PathExclusion
    {
        private readonly List<string> m_Dirs;
        private readonly HashSet<string> m_Files;

        public PathExclusion(IEnumerable<string> excludeDirs, IEnumerable<string> excludeFiles)
        {
            m_Dirs = excludeDirs
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(BuildRevision.WithSeparator)
                .ToList();
            m_Files = new HashSet<string>(
                excludeFiles.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool IsExcluded(string path)
        {
            string full = Path.GetFullPath(path);
            return m_Files.Contains(full) || m_Dirs.Any(dir => full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                                                               || BuildRevision.WithSeparator(full).Equals(dir, StringComparison.OrdinalIgnoreCase));
        }
    }
}
