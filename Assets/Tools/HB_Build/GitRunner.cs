using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// git 명령을 실행하고 표준 출력을 돌려줍니다 (빌드 중 바뀐 파일 보고 · 산출물 이름의 커밋 · 로컬 정리가 같이 씀).
    /// git이 없거나 실패하거나 제한 시간 안에 끝나지 않으면 null.
    /// </summary>
    internal static class GitRunner
    {
        /// <summary> 읽기 명령의 기본 제한 시간 (넘으면 프로세스 종료) </summary>
        public const int TIMEOUT_MS = 15000;

        /// <summary> 되돌릴 수 없는 명령(restore · clean)의 제한 시간. 넘어도 종료하지 않는다 (중간에 끊으면 반쯤 바뀐 상태 · index.lock이 남음) </summary>
        public const int DESTRUCTIVE_TIMEOUT_MS = 10 * 60 * 1000;

        // 이 변수들이 있으면 git이 프로젝트가 아닌 다른 저장소 · 인덱스를 본다 (에디터를 띄운 셸에서 상속될 수 있음)
        private static readonly string[] s_RepoOverrideVariables = { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_COMMON_DIR" };

        public static string Run(string workingDirectory, string arguments)
        {
            return Run(workingDirectory, arguments, out _);
        }

        /// <summary>
        /// 실패하면 null이고 error에 stderr(또는 이유)를 담는다. stdout · stderr를 동시에 비동기로 읽어 파이프가 차서 서로 기다리는 일을 막는다.
        /// killOnTimeout이 false면 시간이 넘어도 git을 끝까지 두고 실패만 알린다.
        /// </summary>
        public static string Run(string workingDirectory, string arguments, out string error, int timeoutMs = TIMEOUT_MS, bool killOnTimeout = true)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo("git", "-c core.quotepath=false " + arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                // 출력 문구를 파싱하므로 번역되지 않게 한다 (한국어 환경의 Git for Windows는 메시지를 번역함)
                psi.EnvironmentVariables["LC_ALL"] = "C";
                psi.EnvironmentVariables["LANGUAGE"] = "C";
                foreach (string name in s_RepoOverrideVariables)
                    psi.EnvironmentVariables.Remove(name);

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        error = "git process did not start";
                        return null;
                    }

                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        if (killOnTimeout)
                        {
                            try { process.Kill(); } catch (Exception) { /* 이미 종료 */ }
                            error = $"git {arguments} timed out ({timeoutMs / 1000}s)";
                        }
                        else
                        {
                            error = $"git {arguments} is still running after {timeoutMs / 1000}s (not stopped). Wait for it to finish, then check 'git status' (a leftover .git/index.lock may need to be removed).";
                        }
                        BuildHelper.LogWarning($"[Git] {error}");
                        return null;
                    }

                    Task.WaitAll(new Task[] { stdout, stderr }, timeoutMs);
                    if (process.ExitCode == 0 && stdout.IsCompleted)
                        return stdout.Result;

                    error = stderr.IsCompleted ? stderr.Result.Trim() : $"git exited with {process.ExitCode}";
                    return null;
                }
            }
            catch (Exception ex)
            {
                // git이 PATH에 없는 경우 등
                error = ex.Message;
                return null;
            }
        }
    }
}
