using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// <c>Assets/__HBTestTemp/&lt;guid&gt;</c>에 임시 에셋 폴더를 만들고 Dispose에서 지웁니다. 마지막 폴더가 지워지면 <c>Assets/__HBTestTemp</c>도 비어 있을 때 지웁니다.
    /// <para>테스트가 중단돼 남은 폴더는 이 도메인에서 처음 만들 때 한 번 지웁니다 — 바로 아래 폴더 중 이 패키지의 표식 파일이 있는 것만 (표식 없는 폴더는 건드리지 않음).</para>
    /// <para>도메인 리로드(스크립트 · asmdef 생성 등으로 인한 컴파일)를 넘는 사용은 지원하지 않습니다. 메인 스레드 전용입니다.
    /// 같은 프로젝트를 여러 에디터가 동시에 여는 경우는 다루지 않습니다 (Unity가 프로젝트를 잠금).</para>
    /// </summary>
    public sealed class TempAssetFolder : IDisposable
    {
        public const string RootPath = "Assets/__HBTestTemp";

        // 점으로 시작하는 파일은 Unity가 임포트하지 않는다 (.meta 없음)
        internal const string MarkerFileName = ".hb_tfh_owner";
        internal const string MarkerContent = "HB_TestFrameworkHelper.TempAssetFolder/1";

        private static readonly HashSet<string> s_Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_Swept;

        private bool m_Disposed;

        /// <summary>예: <c>Assets/__HBTestTemp/0f3c…</c></summary>
        public string AssetPath { get; }

        public TempAssetFolder()
        {
            SweepLeftoversOnce();
            EnsureRoot();

            string guid = AssetDatabase.CreateFolder(RootPath, Guid.NewGuid().ToString("N"));
            AssetPath = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(AssetPath)) throw new IOException($"임시 에셋 폴더를 만들지 못했습니다: {RootPath}");

            s_Active.Add(AssetPath);
            try
            {
                File.WriteAllText(Path.Combine(AssetPath, MarkerFileName), MarkerContent);
            }
            catch (Exception e)
            {
                // 되돌리다 실패해도 원래 예외를 잃지 않는다
                try
                {
                    Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(e, cleanup);
                }
                throw;
            }
        }

        /// <summary>폴더를 지웁니다. 안에 링크(심볼릭 링크 · 정션)가 있으면 따라 들어가 지우지 않도록 지우지 않고 예외를 던집니다.</summary>
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            s_Active.Remove(AssetPath);
            if (AssetDatabase.IsValidFolder(AssetPath))
            {
                if (ContainsLink(AssetPath))
                    throw new InvalidOperationException($"임시 에셋 폴더 안에 링크가 있어 지우지 않았습니다 (링크를 먼저 지우세요): {AssetPath}");
                if (!AssetDatabase.DeleteAsset(AssetPath))
                    throw new IOException($"임시 에셋 폴더를 지우지 못했습니다: {AssetPath}");
            }
            DeleteRootIfEmpty();
        }

        /// <summary>테스트 전용: 이 도메인의 잔여 정리를 다시 돌게 한다.</summary>
        internal static void ResetSweepForTests() => s_Swept = false;

        private static void SweepLeftoversOnce()
        {
            if (s_Swept) return;
            s_Swept = true;
            if (!AssetDatabase.IsValidFolder(RootPath)) return;
            ThrowIfLink(RootPath);

            var failed = new List<string>();
            foreach (string path in AssetDatabase.GetSubFolders(RootPath))
            {
                if (s_Active.Contains(path) || IsLink(path) || !HasOwnerMarker(path)) continue;
                if (ContainsLink(path))
                {
                    Debug.LogWarning($"[HB_TestFrameworkHelper] 남은 임시 에셋 폴더 안에 링크가 있어 지우지 않았습니다: {path}");
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path)) Debug.LogWarning($"[HB_TestFrameworkHelper] 이전 테스트가 남긴 임시 에셋 폴더를 지웠습니다: {path}");
                else failed.Add(path);
            }
            if (failed.Count > 0)
                throw new IOException("이전 테스트가 남긴 임시 에셋 폴더를 지우지 못했습니다: " + string.Join(", ", failed));
        }

        private static void EnsureRoot()
        {
            if (AssetDatabase.IsValidFolder(RootPath))
            {
                ThrowIfLink(RootPath);
                return;
            }
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", "__HBTestTemp")))
                throw new IOException($"임시 에셋 루트 폴더를 만들지 못했습니다: {RootPath}");
        }

        private static void DeleteRootIfEmpty()
        {
            if (s_Active.Count > 0 || !AssetDatabase.IsValidFolder(RootPath)) return;
            // 표식 없는 사용자 폴더 · 파일이 남아 있으면 루트는 그대로 둔다
            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(RootPath).GetEnumerator())
            {
                if (entries.MoveNext()) return;
            }
            AssetDatabase.DeleteAsset(RootPath);
        }

        private static bool HasOwnerMarker(string folder)
        {
            var marker = new FileInfo(Path.Combine(folder, MarkerFileName));
            // 링크 · 큰 파일은 따라가 읽지 않는다
            if (!marker.Exists || (marker.Attributes & FileAttributes.ReparsePoint) != 0 || marker.Length > MaxMarkerBytes) return false;
            return File.ReadAllText(marker.FullName).Trim() == MarkerContent;
        }

        private const int MaxMarkerBytes = 256;

        private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        // 링크 안으로는 들어가지 않고 트리를 훑어 링크가 하나라도 있는지 본다
        private static bool ContainsLink(string folder)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                if ((attributes & FileAttributes.Directory) != 0 && ContainsLink(entry)) return true;
            }
            return false;
        }

        private static void ThrowIfLink(string path)
        {
            if (IsLink(path)) throw new InvalidOperationException($"{path}가 링크(심볼릭 링크 · 정션)라 임시 에셋 폴더로 쓰지 않습니다.");
        }
    }
}
