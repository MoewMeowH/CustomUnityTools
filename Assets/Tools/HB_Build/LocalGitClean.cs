using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// 에디터 로컬 빌드 전에 Unity 프로젝트 폴더를 현재 커밋(HEAD) 상태로 되돌립니다 (Build Manager 창의 "빌드 전 git 작업 폴더 정리").
    /// 지우는 것: 추적 파일의 수정 · 스테이징(HEAD에 없는 스테이징 파일은 삭제), 무시되지 않은 미추적 파일.
    /// 남는 것: Library 등 .gitignore 대상, 빌드 산출물 폴더, 프로젝트 밖 파일, 프로젝트 안 서브모듈의 작업 트리.
    /// 되돌릴 수 없으므로 창의 두 체크박스와 최종 확인 대화상자를 거친 뒤에만 Run을 부른다.
    /// 산출물 폴더를 지킬 수 없는 경우(제외 패턴을 만들 수 없음 · 산출물이 목록에 섞임 · 출력 경로가 소스를 덮음)는 정리를 거부한다.
    /// </summary>
    internal sealed class LocalGitClean
    {
        // 바뀌면 스크립트 컴파일(도메인 리로드)이 일어나 빌드 호출이 끊기는 파일
        private static readonly string[] s_ScriptExtensions = { ".cs", ".asmdef", ".asmref", ".dll", ".rsp" };
        private static readonly string[] s_ScriptFiles = { "Packages/manifest.json", "Packages/packages-lock.json", "ProjectSettings/ProjectSettings.asset", "package.json" };
        // gitignore 패턴에서 뜻이 있는 글자 (산출물 폴더 이름에 있으면 제외 패턴을 정확히 만들 수 없음)
        private static readonly char[] s_PatternChars = { '"', '[', ']', '*', '?', '!', '#', '\\' };
        private const int CONSOLE_LIST_LIMIT = 500;

        private readonly string m_ProjectRoot;
        private readonly string m_ExcludeArgs;
        private readonly List<string> m_Labeled = new List<string>();
        // 스크립트 판정용: 통째로 미추적인 폴더는 안의 파일까지 펼친 목록 (프로젝트 폴더 기준)
        private readonly List<string> m_AffectedFiles = new List<string>();

        public int ModifiedCount { get; private set; }
        public int UntrackedCount { get; private set; }
        public int Count => m_Labeled.Count;

        public bool ChangesScripts => m_AffectedFiles.Any(p =>
            s_ScriptExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            || s_ScriptFiles.Any(f => p.Replace('\\', '/').EndsWith(f, StringComparison.OrdinalIgnoreCase)));

        private LocalGitClean(string projectRoot, string excludeArgs)
        {
            m_ProjectRoot = projectRoot;
            m_ExcludeArgs = excludeArgs;
        }

        /// <summary> 미리보기를 다시 구했을 때 같은 목록인지 (확인 대화상자와 실행 사이에 바뀌었으면 실행하지 않는다) </summary>
        public bool SameAs(LocalGitClean other)
        {
            return other != null && m_Labeled.SequenceEqual(other.m_Labeled, StringComparer.Ordinal);
        }

        /// <summary>
        /// 지울 것을 미리 구한다. 저장 안 한 에셋은 먼저 저장해 목록에 나오게 한다.
        /// 저장 안 한 씬 · 프리팹 편집, git을 쓸 수 없음, 산출물 폴더를 지킬 수 없음이면 null과 이유를 돌려준다.
        /// </summary>
        public static LocalGitClean Prepare(IEnumerable<string> keepDirs, out string error)
        {
            // 에디터 메모리와 디스크가 어긋나면 빌드 결과가 불확실해진다
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    error = $"저장하지 않은 씬이 있습니다: {(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)}\n저장하거나 되돌린 뒤 다시 빌드하세요.";
                    return null;
                }
            }
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
            {
                error = $"저장하지 않은 프리팹 편집이 있습니다: {prefabStage.assetPath}\n저장하거나 되돌린 뒤 다시 빌드하세요.";
                return null;
            }
            // 인스펙터에서 바꾸고 저장하지 않은 에셋(ScriptableObject · 머티리얼 등)을 디스크에 써서 정리 목록에 나오게 한다
            AssetDatabase.SaveAssets();

            string projectRoot = BuildHelper.ResolveProjectRelativePath(".");
            string projectPrefix = BuildRevision.WithSeparator(projectRoot);

            // 프로젝트의 저장소 상대 경로는 git에게 직접 묻는다 (정션 · 링크 · 대소문자로 표기가 달라도 맞음)
            string prefix = GitRunner.Run(projectRoot, "rev-parse --show-prefix", out string gitError)?.Trim();
            if (prefix == null)
            {
                error = $"git을 실행할 수 없거나 프로젝트가 git 저장소가 아닙니다.\n{gitError}";
                return null;
            }
            prefix = prefix.Replace('\\', '/');

            // 산출물 폴더는 지우지 않는다. 지킬 수 없으면 정리하지 않는다 (조용히 정리 대상이 되지 않게)
            var keepList = keepDirs.Where(d => !string.IsNullOrWhiteSpace(d)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var safeKeep = BuildRevision.FilterOutputDirs(keepList, out var covering);
            if (covering.Count > 0)
            {
                error = $"출력 경로가 프로젝트 소스(프로젝트 루트 · Assets · Packages · ProjectSettings)를 덮습니다: {string.Join(", ", covering)}\n출력 경로를 바꾼 뒤 정리하세요.";
                return null;
            }
            var excludes = new StringBuilder();
            foreach (string dir in safeKeep)
            {
                string withSlash = BuildRevision.WithSeparator(dir);
                if (!withSlash.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)) continue;  // 프로젝트 밖은 정리 범위가 아님
                string relative = withSlash.Substring(projectPrefix.Length).Replace('\\', '/').TrimEnd('/');
                if (relative.IndexOfAny(s_PatternChars) >= 0)
                {
                    error = $"출력 폴더 이름에 git 제외 패턴으로 쓸 수 없는 글자가 있어 보호할 수 없습니다: {relative}\n출력 폴더 이름을 바꾸거나 .gitignore에 넣은 뒤 정리하세요.";
                    return null;
                }
                excludes.Append($" -e \"/{prefix}{relative}/\"");
            }

            var clean = new LocalGitClean(projectRoot, excludes.ToString());

            // 추적 파일. 경로는 저장소 루트 기준이고 "-- ." 로 프로젝트 폴더만
            string status = GitRunner.Run(projectRoot, "status --porcelain=v1 -z --untracked-files=no -- .", out gitError);
            if (status == null)
            {
                error = $"git status 실패: {gitError}";
                return null;
            }
            string[] fields = status.Split('\0');
            for (int i = 0; i < fields.Length; i++)
            {
                string entry = fields[i];
                if (entry.Length < 4) continue;
                char x = entry[0];
                string path = entry.Substring(3);
                clean.AddAffected(StripPrefix(path, prefix));
                if (x == 'R' || x == 'C')
                {
                    // 새 경로는 HEAD에 없으므로 지워지고, 원래 경로는 되살아난다
                    clean.Add("[삭제] " + path, modified: true);
                    if (i + 1 < fields.Length && fields[i + 1].Length > 0)
                    {
                        string original = fields[++i];
                        clean.AddAffected(StripPrefix(original, prefix));
                        if (x == 'R') clean.Add("[복원] " + original, modified: true);
                    }
                }
                else if (x == 'A')
                {
                    // 스테이징만 된 새 파일: HEAD에 없으므로 restore가 작업 폴더에서도 지운다
                    clean.Add("[삭제] " + path, modified: true);
                }
                else if (x == 'D' || entry[1] == 'D')
                {
                    // 지운 추적 파일은 다시 생긴다
                    clean.Add("[복원] " + path, modified: true);
                }
                else
                {
                    clean.Add("[수정] " + path, modified: true);
                }
            }

            // 미추적 파일. git clean -n은 "Would remove <경로>"를 프로젝트 폴더 기준으로 출력한다 (영어 출력은 GitRunner가 고정)
            string preview = GitRunner.Run(projectRoot, $"clean -nd{clean.m_ExcludeArgs} -- .", out gitError);
            if (preview == null)
            {
                error = $"git clean 미리보기 실패: {gitError}";
                return null;
            }
            var keepExclusion = new PathExclusion(safeKeep, Enumerable.Empty<string>());
            const string removePrefix = "Would remove ";
            foreach (string line in preview.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (!line.StartsWith(removePrefix, StringComparison.Ordinal)) continue;
                string relative = line.Substring(removePrefix.Length);
                // 제외 패턴이 맞지 않아 산출물이 목록에 들어왔으면 지우지 않는다
                if (keepExclusion.IsExcluded(Path.Combine(projectRoot, relative)))
                {
                    error = $"빌드 산출물 폴더가 정리 목록에 들어왔습니다: {relative}\n산출물을 지키지 못하므로 정리하지 않았습니다. 출력 폴더를 .gitignore에 넣은 뒤 다시 시도하세요.";
                    return null;
                }
                clean.Add("[삭제] " + relative, modified: false);

                if (relative.EndsWith("/"))
                {
                    // 통째로 미추적인 폴더는 한 줄로 나오므로, 스크립트 판정을 위해 안의 파일을 펼친다
                    string inner = GitRunner.Run(projectRoot, $"ls-files --others --exclude-standard -z -- \"{relative.TrimEnd('/')}\"", out _);
                    if (inner == null)
                        clean.AddAffected(relative + "unknown.cs");  // 펼치지 못하면 스크립트가 있다고 본다 (안전한 쪽)
                    else
                        foreach (string file in inner.Split('\0').Where(f => f.Length > 0))
                            clean.AddAffected(StripPrefix(file, prefix));
                }
                else
                {
                    clean.AddAffected(relative);
                }
            }

            error = null;
            return clean;
        }

        private void Add(string labeled, bool modified)
        {
            m_Labeled.Add(labeled);
            if (modified) ModifiedCount++;
            else UntrackedCount++;
        }

        private void AddAffected(string projectRelative) => m_AffectedFiles.Add(projectRelative);

        // status · ls-files 경로(저장소 루트 기준)를 프로젝트 폴더 기준으로
        private static string StripPrefix(string repoRelative, string prefix)
        {
            return prefix.Length > 0 && repoRelative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? repoRelative.Substring(prefix.Length)
                : repoRelative;
        }

        /// <summary> 대화상자 · 로그용 목록 (앞 max개) </summary>
        public string Describe(int max)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"되돌리거나 지울 추적 파일 {ModifiedCount}개, 지울 미추적 파일 · 폴더 {UntrackedCount}개");
            foreach (string line in m_Labeled.Take(max))
                sb.AppendLine("  " + line);
            if (m_Labeled.Count > max)
                sb.AppendLine($"  ... 외 {m_Labeled.Count - max}개");
            return sb.ToString().TrimEnd();
        }

        /// <summary> Console에 남길 목록 (너무 길어지지 않게 상한) </summary>
        public string DescribeForConsole() => Describe(CONSOLE_LIST_LIMIT);

        /// <summary>
        /// 추적 파일을 HEAD로 되돌리고(스테이징 포함) 미추적 파일을 지운다. -x 없이 (무시 파일 유지).
        /// 오래 걸려도 중간에 끊지 않는다 (반쯤 바뀐 상태 · index.lock 방지).
        /// </summary>
        public bool Run(out string error)
        {
            if (ModifiedCount > 0 && GitRunner.Run(m_ProjectRoot, "restore --source=HEAD --staged --worktree -- .", out string restoreError,
                    GitRunner.DESTRUCTIVE_TIMEOUT_MS, killOnTimeout: false) == null)
            {
                error = $"git restore 실패: {restoreError}";
                return false;
            }
            if (UntrackedCount > 0 && GitRunner.Run(m_ProjectRoot, $"clean -fd{m_ExcludeArgs} -- .", out string cleanError,
                    GitRunner.DESTRUCTIVE_TIMEOUT_MS, killOnTimeout: false) == null)
            {
                error = $"git clean 실패 (추적 파일 되돌리기는 이미 끝남): {cleanError}";
                return false;
            }
            error = null;
            return true;
        }
    }
}
