using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HB.Cheat.Editor
{
    /// <summary>
    /// 1.0.x는 HB_CHEAT와 프로젝트 심볼(설정의 User Define, 기본 DEV_CHEAT)을 함께 썼다. 1.1.0부터 HB_CHEAT 하나만 쓴다.
    /// 프로젝트마다 한 번: HB_CHEAT와 함께 켜진 옛 프로젝트 심볼을 모든 플랫폼 · DefineContainer에서 지우고,
    /// 옛 이름이 남은 치트 파일(#if) · asmdef(defineConstraints)를 경고로 알린다 (파일은 고치지 않는다).
    /// 완료 표시(ProjectSettings 파일, Git으로 공유)가 있으면 하지 않는다. 배치 모드(CI 빌드)에서는 하지 않는다 (빌드 중에 심볼이 바뀌면 안 된다).
    /// </summary>
    internal static class CheatDefineMigration
    {
        public const string MarkerPath = "ProjectSettings/HB_Cheat.DefineMerged.txt";

        [InitializeOnLoadMethod]
        private static void Bootstrap()
        {
            if (File.Exists(MarkerPath) || Application.isBatchMode) return;
            EditorApplication.delayCall += Run;
        }

        /// <summary>지울 옛 심볼 이름. 비었으면 1.0.x 기본값, 심볼로 못 쓰는 이름이면 1.0.x처럼 기본값, HB_CHEAT면 null (지울 것 없음).</summary>
        internal static string ResolveLegacyDefine(string assetValue)
        {
            string value = assetValue?.Trim();
            if (string.IsNullOrEmpty(value) || !CheatSettings.IsValidDefine(value)) value = CheatSettings.LegacyDefaultUserDefine;
            return value == CheatDefines.CoreDefine ? null : value;
        }

        /// <summary>HB_CHEAT와 옛 심볼이 함께 있을 때만 옛 심볼을 뺀 목록. 아니면 null.</summary>
        internal static string[] WithoutLegacy(string[] defines, string legacy)
        {
            if (defines == null || legacy == null) return null;
            if (Array.IndexOf(defines, CheatDefines.CoreDefine) < 0 || Array.IndexOf(defines, legacy) < 0) return null;
            return defines.Where(d => d != legacy).ToArray();
        }

        /// <summary>#if · #elif 줄에 심볼이 낱말로 들어 있는지 (DEV_CHEATX 같은 다른 이름은 아니다)</summary>
        internal static bool IsGuardLine(string line, string symbol)
        {
            if (line == null) return false;
            return Regex.IsMatch(line, @"^\s*#\s*(if|elif)\b.*(?<![A-Za-z0-9_])" + Regex.Escape(symbol) + @"(?![A-Za-z0-9_])");
        }

        /// <summary>[Conditional("심볼")] 특성 (호출만 에러 없이 사라진다)</summary>
        internal static bool IsConditionalLine(string line, string symbol)
        {
            if (line == null) return false;
            return Regex.IsMatch(line, @"\bConditional(Attribute)?\s*\(\s*""" + Regex.Escape(symbol) + @"""\s*\)");
        }

        [Serializable]
        private sealed class AsmdefConstraints
        {
            public string[] defineConstraints;
        }

        internal static bool AsmdefRequires(string asmdefJson, string symbol)
        {
            try
            {
                var data = JsonUtility.FromJson<AsmdefConstraints>(asmdefJson);
                // 식(HB_CHEAT || DEV_CHEAT)도 본다. !DEV_CHEAT(없을 때만)는 요구가 아니다
                var pattern = new Regex(@"(?<![A-Za-z0-9_!])" + Regex.Escape(symbol) + @"(?![A-Za-z0-9_])");
                return data?.defineConstraints != null && Array.Exists(data.defineConstraints, c => c != null && pattern.IsMatch(c));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static void Run()
        {
            if (File.Exists(MarkerPath)) return;

            // 임포트 · 컴파일 중이면 에셋(설정 · DefineContainer)을 아직 다 모를 수 있다 → 끝난 뒤 다시
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += Run;
                return;
            }

            try
            {
                var settings = CheatSettings.Load();
                // 설정 에셋 파일은 있는데 아직 읽지 못했다 (첫 임포트 등) → 옛 이름을 기본값으로 잘못 고르지 않도록 다음 리로드에 다시
                if (!CheatSettings.AssetExists && File.Exists(CheatSettings.DefaultAssetPath)) return;

                string legacy = ResolveLegacyDefine(settings.LegacyUserDefine);
                // 설정 에셋에 옛 값이 있으면 1.0.x를 쓰던 프로젝트 (1.1.0에서 만든 에셋은 비어 있다)
                bool usedLegacy = legacy != null && RemoveLegacy(legacy, usedLegacySettings: settings.LegacyUserDefine.Length > 0);

                // 1.0.x 레지스트리는 #if HB_CHEAT && <옛 심볼>로 감싸 있다. 자동 생성이 꺼져 있으면 여기서 한 번 다시 만든다
                if (usedLegacy && !settings.AutoGenerate && CheatEditorHooks.IsGeneratorAvailable)
                    CheatEditorHooks.Generate(true);

                settings.ClearLegacyUserDefine();
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                    AssetDatabase.SaveAssetIfDirty(settings);

                // 표시를 먼저 쓰고 에디터가 비정상 종료되면 심볼은 저장되지 않은 채 다시 시도하지 않는다 → 저장 뒤 표시
                AssetDatabase.SaveAssets();
                WriteMarker();
            }
            catch (Exception ex)
            {
                // 표시를 쓰지 않았으므로 다음 리로드에서 다시 시도한다
                Debug.LogWarning($"[HB_Cheat] Failed to remove the old project cheat define: {ex.Message}");
            }
        }

        /// <summary>옛 심볼을 지우고 알린다. 1.0.x를 쓴 흔적이 있으면 true.</summary>
        private static bool RemoveLegacy(string legacy, bool usedLegacySettings)
        {
            var removedFrom = new List<string>();
            foreach (var target in CheatDefines.GetAllNamedTargets())
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
                var updated = WithoutLegacy(defines, legacy);
                if (updated == null) continue;
                PlayerSettings.SetScriptingDefineSymbols(target, updated);
                removedFrom.Add(target.TargetName);
            }

            int containerEntries = CheatBuildDefineSync.RewriteEntries((path, buildType, platformType, defines) => WithoutLegacy(defines, legacy));
            if (containerEntries > 0) removedFrom.Add($"DefineContainer ({containerEntries} entries)");

            // 1.0.x를 쓴 흔적이 없으면 옛 이름을 치트 밖 용도로 쓰는 프로젝트일 수 있다 → 파일 목록을 내지 않는다
            bool usedLegacy = usedLegacySettings || removedFrom.Count > 0;
            var references = usedLegacy ? FindLegacyReferences(legacy) : new List<string>();
            if (usedLegacy && CheatDefines.GetActiveProfileDefines().Contains(legacy))
                references.Add($"Active Build Profile scripting defines ({legacy})");
            if (removedFrom.Count == 0 && references.Count == 0) return usedLegacy;

            var sb = new StringBuilder();
            sb.Append($"[HB_Cheat] Cheat defines are merged into {CheatDefines.CoreDefine} (1.1.0).");
            if (removedFrom.Count > 0)
                sb.Append($" Removed old project define {legacy} from: {string.Join(", ", removedFrom)}.");
            if (references.Count > 0)
            {
                sb.Append($"\nThese still use {legacy} (#if, [Conditional], asmdef defineConstraints) and are compiled only when {legacy} is defined. Replace {legacy} with {CheatDefines.CoreDefine}:\n");
                sb.Append(string.Join("\n", references.Select(r => "  - " + r)));
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log(sb.ToString());
            }
            return usedLegacy;
        }

        /// <summary>
        /// Assets 아래에서 옛 심볼을 #if · [Conditional]로 쓰는 .cs(생성 레지스트리 제외, 다시 생성된다)와 defineConstraints로 요구하는 asmdef.
        /// 읽지 못한 파일 · 폴더는 건너뛰고 목록 끝에 적는다 (스캔 실패로 정리 전체가 리로드마다 다시 돌지 않도록).
        /// </summary>
        private static List<string> FindLegacyReferences(string legacy)
        {
            var result = new List<string>();
            int unreadable = 0;
            foreach (var path in EnumerateSafe("*.cs", ref unreadable))
            {
                string[] lines;
                try { lines = File.ReadAllLines(path); }
                catch (Exception) { unreadable++; continue; }
                if (lines.Any(l => l.Contains(CheatFileTemplates.GeneratedMarker))) continue;
                if (lines.Any(l => IsGuardLine(l, legacy) || IsConditionalLine(l, legacy))) result.Add(path.Replace('\\', '/'));
            }

            foreach (var path in EnumerateSafe("*.asmdef", ref unreadable))
            {
                string json;
                try { json = File.ReadAllText(path); }
                catch (Exception) { unreadable++; continue; }
                if (AsmdefRequires(json, legacy)) result.Add(path.Replace('\\', '/'));
            }

            if (unreadable > 0) result.Add($"({unreadable} files or folders could not be read and were not checked)");
            return result;
        }

        private static List<string> EnumerateSafe(string pattern, ref int unreadable)
        {
            try { return Directory.EnumerateFiles("Assets", pattern, SearchOption.AllDirectories).ToList(); }
            catch (Exception) { unreadable++; return new List<string>(); }
        }

        private static void WriteMarker()
        {
            // 쓰다가 실패해도 반쯤 쓴 표시가 남지 않도록 임시 파일에 쓴 뒤 옮긴다
            string tempPath = MarkerPath + ".tmp";
            File.WriteAllText(tempPath,
                "HB_Cheat: the old project cheat define (1.0.x User Define) was removed once for this project; cheats now use HB_CHEAT only.\n" +
                "It will not run again while this file exists.\n", new UTF8Encoding(false));
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            File.Move(tempPath, MarkerPath);
        }
    }
}
