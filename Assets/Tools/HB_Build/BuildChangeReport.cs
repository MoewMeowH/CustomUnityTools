using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 빌드 전후 `git status`를 비교해 빌드 중 새로 바뀐 파일(ProjectSettings 재저장 등)을 로그로 알립니다.
    /// 자동으로 되돌리지 않습니다. 사람이 직접 확인하고 되돌리게 하기 위한 목록입니다.
    /// git이 없거나 저장소가 아니거나 시간 안에 끝나지 않으면 아무것도 하지 않습니다.
    /// </summary>
    internal sealed class BuildChangeReport
    {
        private const int GIT_TIMEOUT_MS = 15000;

        // git status 경로는 저장소 루트 기준이므로 (프로젝트가 저장소 하위 폴더일 수 있음) 루트를 따로 구한다
        private readonly string m_RepoRoot;
        private readonly Dictionary<string, (string Status, DateTime WriteTime)> m_Before;

        private BuildChangeReport(string repoRoot, Dictionary<string, (string Status, DateTime WriteTime)> before)
        {
            m_RepoRoot = repoRoot;
            m_Before = before;
        }

        public static BuildChangeReport Capture()
        {
            string projectRoot = BuildHelper.ResolveProjectRelativePath(".");
            string repoRoot = RunGit(projectRoot, "rev-parse --show-toplevel")?.Trim();
            var before = string.IsNullOrEmpty(repoRoot) ? null : ReadStatus(repoRoot);
            if (before == null)
            {
                BuildHelper.Log("[Changes] git status unavailable. Skipping the list of files changed by the build.");
                return null;
            }
            return new BuildChangeReport(Path.GetFullPath(repoRoot), before);
        }

        /// <summary>
        /// 빌드 중 상태가 바뀐 파일(새로 바뀜, 다시 쓰임, 원래대로 돌아감)을 로그로 출력합니다.
        /// excludeDirs(빌드 산출물 폴더) 안의 파일과 excludeFiles(빌드 로그)는 제외합니다.
        /// </summary>
        public void LogChangesDuringBuild(IEnumerable<string> excludeDirs, IEnumerable<string> excludeFiles)
        {
            var after = ReadStatus(m_RepoRoot);
            if (after == null) return;

            var dirs = excludeDirs
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .ToList();
            var files = new HashSet<string>(
                excludeFiles.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            var changed = new List<string>();
            // 빌드 전에만 있던 경로(빌드 중 원래대로 돌아간 파일)도 보도록 전후 합집합을 본다
            foreach (string path in m_Before.Keys.Union(after.Keys))
            {
                string full = Path.GetFullPath(Path.Combine(m_RepoRoot, path));
                if (files.Contains(full) || dirs.Any(dir => full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
                    continue;

                bool hadBefore = m_Before.TryGetValue(path, out var prev);
                bool hasAfter = after.TryGetValue(path, out var now);

                if (!hasAfter)
                    changed.Add($"{prev.Status} -> clean  {path}");
                else if (!hadBefore || prev.Status != now.Status || prev.WriteTime != now.WriteTime)
                    changed.Add($"{now.Status} {path}");
            }

            if (changed.Count == 0)
            {
                BuildHelper.Log("[Changes] No files in the repository were changed by the build.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[Changes] {changed.Count} file(s) changed during the build. Review them and revert manually if the change is not intended (not reverted automatically):");
            foreach (string line in changed.OrderBy(x => x, StringComparer.Ordinal))
                sb.AppendLine($"  {line}");
            BuildHelper.LogWarning(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// 저장소 루트 기준 경로 → (상태 코드, 마지막 수정 시각). 실패하면 null.
        /// -z(NUL 구분)로 받아 경로 인용 · 이스케이프 없이 읽고, rename/copy는 다음 필드(원래 경로)를 건너뛴다.
        /// </summary>
        private static Dictionary<string, (string Status, DateTime WriteTime)> ReadStatus(string repoRoot)
        {
            string output = RunGit(repoRoot, "status --porcelain=v1 -z --untracked-files=all");
            if (output == null) return null;

            var result = new Dictionary<string, (string Status, DateTime WriteTime)>(StringComparer.Ordinal);
            string[] fields = output.Split('\0');
            for (int i = 0; i < fields.Length; i++)
            {
                string entry = fields[i];
                if (entry.Length < 4) continue;

                string status = entry.Substring(0, 2);
                string path = entry.Substring(3);
                // R(이름 변경) · C(복사)는 새 경로 다음 필드에 원래 경로가 온다
                if (status[0] == 'R' || status[0] == 'C')
                    i++;

                string full = Path.Combine(repoRoot, path);
                DateTime writeTime = File.Exists(full) ? File.GetLastWriteTimeUtc(full) : DateTime.MinValue;
                result[path] = (status, writeTime);
            }
            return result;
        }

        /// <summary>
        /// git 명령의 표준 출력. git이 없거나 실패하거나 GIT_TIMEOUT_MS 안에 끝나지 않으면 null (시간 초과 시 프로세스 종료).
        /// stdout · stderr를 동시에 비동기로 읽어 파이프가 차서 서로 기다리는 일을 막는다.
        /// </summary>
        private static string RunGit(string workingDirectory, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("git", "-c core.quotepath=false " + arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return null;

                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(GIT_TIMEOUT_MS))
                    {
                        try { process.Kill(); } catch (Exception) { /* 이미 종료 */ }
                        BuildHelper.LogWarning($"[Changes] git {arguments} timed out ({GIT_TIMEOUT_MS / 1000}s). Skipping the list of changed files.");
                        return null;
                    }

                    Task.WaitAll(new Task[] { stdout, stderr }, GIT_TIMEOUT_MS);
                    return process.ExitCode == 0 && stdout.IsCompleted ? stdout.Result : null;
                }
            }
            catch (Exception)
            {
                // git이 PATH에 없는 경우 등
                return null;
            }
        }
    }
}
