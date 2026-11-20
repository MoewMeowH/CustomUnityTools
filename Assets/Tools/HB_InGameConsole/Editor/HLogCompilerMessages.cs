#if USE_HLOG
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace InGameConsoleSystem.Editor
{
    /// <summary>
    /// 컴파일 경고 · 오류(Roslyn 분석기 진단 포함)를 도메인 리로드 너머로 HLog에 남긴다.
    /// 컴파일 메시지는 리로드 전 옛 도메인의 로그 콜백으로 들어오고 리로드와 함께 지워지므로,
    /// 어셈블리별로 SessionState에 두었다가 리로드 직후 다시 넣는다. 다시 컴파일된 어셈블리의 목록만 바뀐다 (깨끗하면 비워진다).
    /// 에디트 모드용이다: Play 진입 · Play 중 리로드에는 다시 넣지 않는다.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class HLogCompilerMessages
    {
        internal const string SessionKey = "HB.InGameConsole.CompilerMessages";

        [Serializable]
        internal sealed class Item
        {
            public string assembly;
            public string message;
            public LogType type;
        }

        [Serializable]
        private sealed class Store
        {
            public List<Item> items = new List<Item>();
        }

        static HLogCompilerMessages()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 지워졌거나 심볼로 빠진 어셈블리, 플레이어 빌드 컴파일 결과는 다시 컴파일되지 않아 목록이 바뀌지 않는다 → 여기서 버린다
            var existing = new HashSet<string>();
            foreach (var assembly in CompilationPipeline.GetAssemblies())
            {
                existing.Add(assembly.outputPath);
            }
            var items = KeepExisting(Load(), existing);
            Save(items);
            foreach (var item in items)
            {
                HLogCore.AddExternalEntry(item.message, item.type);
            }
        }

        private static void OnAssemblyCompilationFinished(string assembly, CompilerMessage[] messages)
        {
            Save(Replace(Load(), assembly, messages));
        }

        /// <summary>그 어셈블리의 항목을 이번 메시지(경고 · 오류만)로 바꾼다. 다른 어셈블리 항목은 그대로 둔다.</summary>
        internal static List<Item> Replace(List<Item> current, string assembly, CompilerMessage[] messages)
        {
            var result = new List<Item>(current.Count + messages.Length);
            foreach (var item in current)
            {
                if (item.assembly != assembly) result.Add(item);
            }
            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Info) continue;
                result.Add(new Item
                {
                    assembly = assembly,
                    message = message.message,
                    type = message.type == CompilerMessageType.Error ? LogType.Error : LogType.Warning
                });
            }
            return result;
        }

        /// <summary>지금 에디터 컴파일 대상인 어셈블리(출력 경로)의 항목만 남긴다.</summary>
        internal static List<Item> KeepExisting(List<Item> current, HashSet<string> existingOutputPaths)
        {
            return current.FindAll(item => existingOutputPaths.Contains(item.assembly));
        }

        private static List<Item> Load()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return new List<Item>();
            return JsonUtility.FromJson<Store>(json).items;
        }

        private static void Save(List<Item> items)
        {
            if (items.Count == 0)
            {
                SessionState.EraseString(SessionKey);
                return;
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new Store { items = items }));
        }
    }
}
#endif
