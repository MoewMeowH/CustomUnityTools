using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>
    /// 바깥 TempScene으로 지금 열린 씬 구성을 보호한 뒤 그 안에서 확인한다.
    /// 저장하지 않은 씬이 열려 있으면 바깥 TempScene부터 Inconclusive가 된다 (의도한 동작).
    /// </summary>
    public class TempSceneTests
    {
        [Test]
        public void OnlyUntitledCleanScene_ReturnsToNewEmptyScene()
        {
            using (new TempScene())
            {
                using (var inner = new TempScene(NewSceneSetup.DefaultGameObjects))
                {
                    Assert.Greater(inner.Scene.rootCount, 0);
                }

                Assert.AreEqual(1, SceneManager.sceneCount);
                Scene active = SceneManager.GetActiveScene();
                Assert.IsTrue(string.IsNullOrEmpty(active.path));
                Assert.AreEqual(0, active.rootCount);
            }
        }

        [Test]
        public void DirtySceneOpen_IsInconclusive_WithoutChangingScenes()
        {
            using (new TempScene())
            {
                new GameObject("HBTempScene_Dirty");
                Scene before = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(before);

                Assert.Throws<InconclusiveException>(() => new TempScene());

                Scene after = SceneManager.GetActiveScene();
                Assert.AreEqual(before.handle, after.handle);
                Assert.IsTrue(after.isDirty);
                Assert.IsTrue(GameObject.Find("HBTempScene_Dirty") != null);
            }
        }

        [Test]
        public void SavedAndUntitledMixed_RestoresSavedSceneOnly()
        {
            var scope = new TestScope();
            try
            {
                TempAssetFolder folder = scope.NewTempAssetFolder();
                // 바깥 TempScene이 폴더보다 먼저 정리된다 (역순) — 저장 씬을 닫은 뒤 폴더를 지움
                scope.NewTempScene();

                string path = folder.AssetPath + "/Saved.unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.AreEqual(2, SceneManager.sceneCount);

                using (new TempScene())
                {
                    Assert.AreEqual(1, SceneManager.sceneCount);
                }

                Assert.AreEqual(1, SceneManager.sceneCount, "경로 없는 씬은 되돌리지 않음");
                Assert.AreEqual(path, SceneManager.GetActiveScene().path);
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
