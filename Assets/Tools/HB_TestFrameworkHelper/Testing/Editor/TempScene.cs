using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// 지금 열린 씬 구성을 기억하고 새 씬을 연 뒤, Dispose에서 되돌립니다. EditMode 전용 (PlayMode에서 만들면 예외).
    /// <list type="bullet">
    /// <item>저장하지 않은 변경이 있는 씬이나 Prefab Mode 프리팹이 열려 있으면 아무것도 바꾸지 않고 <c>Assert.Inconclusive</c>로 끝냅니다 (사용자 변경을 저장하지도 버리지도 않음).</item>
    /// <item>저장된 적 없는(경로 없는) 씬은 되돌릴 수 없어 빼고 저장 씬만 되돌립니다. 저장 씬이 하나도 없었으면 새 빈 씬을 엽니다 (배치 모드 · 새 에디터의 Untitled 상태).</item>
    /// <item>테스트가 임시 씬에 한 변경은 저장하지 않고 버립니다.</item>
    /// </list>
    /// </summary>
    public sealed class TempScene : IDisposable
    {
        private readonly SceneSetup[] m_Restore;
        private bool m_Disposed;

        public Scene Scene { get; }

        public TempScene(NewSceneSetup setup = NewSceneSetup.EmptyScene)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("TempScene은 EditMode 전용입니다 (PlayMode에서는 씬을 바꾸지 않습니다).");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                    Assert.Inconclusive($"저장하지 않은 변경이 있는 씬이 열려 있어 씬을 바꾸지 않습니다: {DisplayName(open)}");
            }
            // 프리팹 편집 화면(Prefab Mode)의 씬은 위 목록에 없다 — 새 씬을 열면 닫히므로 같은 규칙으로 막는다
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
                Assert.Inconclusive($"저장하지 않은 변경이 있는 프리팹이 Prefab Mode로 열려 있어 씬을 바꾸지 않습니다: {prefabStage.assetPath}");

            m_Restore = SavedScenesOnly(EditorSceneManager.GetSceneManagerSetup());
            Scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            if (m_Restore.Length == 0) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else EditorSceneManager.RestoreSceneManagerSetup(m_Restore);
        }

        private static SceneSetup[] SavedScenesOnly(SceneSetup[] setups)
        {
            var saved = new List<SceneSetup>();
            if (setups != null)
            {
                foreach (SceneSetup s in setups)
                {
                    if (s != null && !string.IsNullOrEmpty(s.path)) saved.Add(s);
                }
            }
            if (saved.Count == 0) return saved.ToArray();

            // 원래 활성 씬이 경로 없는 씬이었으면, 저장 씬 중 원래 순서로 처음 로드된 것을 활성으로 (로드된 게 없으면 첫 씬을 로드)
            if (!saved.Exists(s => s.isActive))
            {
                SceneSetup first = saved.Find(s => s.isLoaded) ?? saved[0];
                first.isLoaded = true;
                first.isActive = true;
            }
            return saved.ToArray();
        }

        private static string DisplayName(Scene scene) => string.IsNullOrEmpty(scene.path) ? $"{scene.name} (저장 안 됨)" : scene.path;
    }
}
