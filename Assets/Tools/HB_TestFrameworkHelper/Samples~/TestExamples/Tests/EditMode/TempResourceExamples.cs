using System.IO;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HB.TestFrameworkHelper.Samples.Tests
{
    /// <summary>
    /// 에디터 전용 임시 자원 (<c>HB.TestFrameworkHelper.Testing.Editor</c> 참조 필요).
    /// <c>Scope.NewTemp…()</c>로 만들면 Scope에 맡겨져 테스트가 끝날 때 지워집니다.
    /// </summary>
    public class TempResourceExamples : HBTestFixture
    {
        [Test]
        public void TempDirectory_SaveAndLoad()
        {
            // OS 임시 폴더 아래 HBTest_<guid>. 테스트가 끝나면 폴더째 지웁니다
            TempDirectory dir = Scope.NewTempDirectory();

            string path = ExampleSaveFile.Save(dir.FullPath, "Tester", 7);

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(ExampleSaveFile.TryLoad(dir.FullPath, out string name, out int level));
            Assert.AreEqual("Tester", name);
            Assert.AreEqual(7, level);
        }

        [Test]
        public void Own_FileIsClosedBeforeFolderIsDeleted()
        {
            TempDirectory dir = Scope.NewTempDirectory();

            // Own으로 맡긴 IDisposable도 같은 스택에서 정리됩니다.
            // 나중에 등록한 writer가 먼저 닫히고, 그 뒤 폴더가 지워집니다 (열린 파일이 있으면 Windows에서 폴더를 못 지움)
            StreamWriter writer = Scope.Own(File.CreateText(Path.Combine(dir.FullPath, "log.txt")));
            writer.WriteLine("hello");
            writer.Flush();

            Assert.AreEqual(1, Directory.GetFiles(dir.FullPath).Length);
        }

        [Test]
        public void TempAssetFolder_CreateAndLoadAsset()
        {
            // Assets/__HBTestTemp/<guid>. 테스트가 끝나면 지우고, 루트가 비면 루트도 지웁니다
            TempAssetFolder folder = Scope.NewTempAssetFolder();

            var config = Scope.NewInstance<ExampleItemConfig>();
            config.MaxStack = 20;
            string assetPath = folder.AssetPath + "/ItemConfig.asset";

            // 에셋이 된 객체는 Scope가 지우지 않고, 폴더를 지울 때 파일째 지워집니다
            AssetDatabase.CreateAsset(config, assetPath);

            var loaded = AssetDatabase.LoadAssetAtPath<ExampleItemConfig>(assetPath);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(20, loaded.MaxStack);
        }

        [Test]
        public void TempScene_BuildSceneInIsolation()
        {
            // 지금 열린 씬 구성을 기억하고 빈 씬을 엽니다. 테스트가 끝나면 원래 씬 구성으로 돌아갑니다.
            // 저장하지 않은 변경이 있는 씬이 열려 있으면 아무것도 바꾸지 않고 Inconclusive로 끝납니다.
            TempScene temp = Scope.NewTempScene();
            Assert.AreEqual(temp.Scene, SceneManager.GetActiveScene());

            // 임시 씬에 만든 객체는 씬과 함께 버려집니다 (Scope에 등록하지 않아도 됨)
            var enemy = new GameObject("Enemy", typeof(ExampleHealth));
            enemy.GetComponent<ExampleHealth>().Configure(maxHp: 30, destroyDelay: 0f);

            Assert.AreEqual(1, temp.Scene.rootCount);
            Assert.AreEqual("Enemy", temp.Scene.GetRootGameObjects()[0].name);
        }
    }
}
