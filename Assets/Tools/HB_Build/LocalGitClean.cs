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
        private static readonly string[] s_ScriptFiles = { "Packages/manifest.json", "Packages/packages-lock.json", "package.json" };
        // 프로젝트 설정은 에디터가 메모리에 들고 있어 디스크만 되돌리면 빌드가 메모리 값으로 될 수 있다 → 빌드를 멈추고 다시 누르게 한다
        private const string PROJECT_SETTINGS_DIR = "ProjectSettings/";
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

        /// <summary> 정리 뒤 바로 빌드하면 안 되는 변경 (스크립트 컴파일 · 패키지 해석 · 프로젝트 설정 다시 읽기가 필요) </summary>
        public bool ChangesScripts => m_AffectedFiles.Any(p =>
            s_ScriptExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            || s_ScriptFiles.Any(f => p.Replace('\\', '/').EndsWith(f, StringComparison.OrdinalIgnoreCase))
            || p.Replace('\\', '/').StartsWith(PROJECT_SETTINGS_DIR, StringComparison.OrdinalIgnoreCase));

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
        /// 지울 것을 미리 구한다. 읽기만 하고 아무것도 저장하지 않는다.
        /// Play 모드 · 컴파일 중, 저장 안 한 씬 · 프리팹 편집 · 에셋, git을 쓸 수 없음, 산출물 폴더를 지킬 수 없음이면 null과 이유를 돌려준다.
        /// </summary>
        public static LocalGitClean Prepare(IEnumerable<string> keepDirs, out string error)
        {
            // Play 모드에서는 플레이어 빌드가 안 되므로 되돌릴 수 없는 정리만 끝나게 된다
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                error = "Play 모드이거나 스크립트를 컴파일하는 중입니다.\nPlay 모드를 끝내고 컴파일이 끝난 뒤 다시 빌드하세요.";
                return null;
            }

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
            // 인스펙터에서 바꾸고 저장하지 않은 에셋(ScriptableObject · 머티리얼 · 프로젝트 설정 등)도 씬처럼 거부한다.
            // 자동으로 저장하면 취소 · 거부돼도 저장이 남고, 정리 범위 밖(프로젝트 밖 패키지 · 무시 파일)이면 되돌려지지도 않는다
            var unsaved = FindUnsavedAssets();
            if (unsaved.Count > 0)
            {
                const int shown = 10;
                error = $"저장하지 않은 에셋이 {unsaved.Count}개 있습니다. 저장하거나 되돌린 뒤 다시 빌드하세요.\n  "
                        + string.Join("\n  ", unsaved.Take(shown))
                        + (unsaved.Count > shown ? $"\n  ... 외 {unsaved.Count - shown}개" : string.Empty);
                return null;
            }

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
            // 줄바꿈만 바뀌었으면 컴파일이 일어나지 않으므로 스크립트 변경으로 보지 않을 후보 (Unity가 줄바꿈만 바꿔 자주 다시 저장함)
            var eolOnlyCandidates = new List<(string RepoPath, string ProjectPath)>();
            string[] fields = status.Split('\0');
            for (int i = 0; i < fields.Length; i++)
            {
                string entry = fields[i];
                if (entry.Length < 4) continue;
                char x = entry[0];
                string path = entry.Substring(3);
                clean.AddAffected(StripPrefix(path, prefix));
                // 프로젝트 설정은 Unity가 줄바꿈만 바꿔 자주 다시 저장하므로, 줄바꿈만 다르면 멈출 이유가 없다
                // (스크립트 · 패키지 파일은 바이트가 바뀌면 컴파일되므로 해당 없음)
                string projectPath = StripPrefix(path, prefix);
                if ((x == 'M' || entry[1] == 'M') && projectPath.StartsWith(PROJECT_SETTINGS_DIR, StringComparison.OrdinalIgnoreCase))
                    eolOnlyCandidates.Add((path, projectPath));
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

            if (eolOnlyCandidates.Count > 0)
            {
                // 줄바꿈 차이를 무시하고도 바뀐 파일만 남는다 (경로는 저장소 루트 기준). 실패하면 스크립트 변경으로 둔다 (안전한 쪽)
                string pathspec = string.Join(" ", eolOnlyCandidates.Select(c => $"\"{c.ProjectPath}\""));
                string realChanges = GitRunner.Run(projectRoot, $"diff --ignore-cr-at-eol --name-only HEAD -- {pathspec}", out _);
                if (realChanges != null)
                {
                    var changed = new HashSet<string>(realChanges.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase);
                    foreach (var candidate in eolOnlyCandidates.Where(c => !changed.Contains(c.RepoPath)))
                        clean.m_AffectedFiles.Remove(candidate.ProjectPath);
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

        /// <summary> 메모리에서 바뀌고 디스크에 저장되지 않은 에셋 경로 (로드된 영구 객체 중 dirty이고 정리 범위 안인 것) </summary>
        internal static List<string> FindUnsavedAssets()
        {
            string projectPrefix = BuildRevision.WithSeparator(BuildHelper.ResolveProjectRelativePath("."));
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            var checkedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (obj == null || !EditorUtility.IsPersistent(obj) || !EditorUtility.IsDirty(obj)) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (checkedPaths.Add(path) && IsInCleanScope(path, projectPrefix))
                    paths.Add(path);
            }
            return paths.ToList();
        }

        /// <summary> 에셋 경로의 실제 파일이 프로젝트 폴더 안(정리가 되돌리는 범위)인지 </summary>
        /// <remarks>
        /// 레지스트리 · 내장 · git 패키지(Library/PackageCache)는 읽기 전용이고 정리 대상도 아니다.
        /// URP처럼 셰이더를 컴파일하는 패키지는 열자마자 셰이더가 dirty로 잡히므로, 넣으면 정리가 항상 거부된다.
        /// 프로젝트 밖 로컬(file:) 패키지도 정리가 건드리지 않으므로 빼낸다
        /// </remarks>
        internal static bool IsInCleanScope(string assetPath, string projectPrefix)
        {
            // 내장 리소스 · Library 안 객체는 사용자가 저장하는 대상이 아니다
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal) || assetPath.StartsWith("ProjectSettings/", StringComparison.Ordinal))
                return true;
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return false;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null)
                return true;  // 패키지를 못 찾으면 프로젝트 안으로 본다 (안전한 쪽: 거부)
            if (package.source != UnityEditor.PackageManager.PackageSource.Embedded
                && package.source != UnityEditor.PackageManager.PackageSource.Local)
                return false;
            return string.IsNullOrEmpty(package.resolvedPath)
                   || BuildRevision.WithSeparator(package.resolvedPath).StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase);
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
