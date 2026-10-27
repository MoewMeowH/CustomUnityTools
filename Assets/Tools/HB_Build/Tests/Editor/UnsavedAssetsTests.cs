using System.Collections.Generic;
using System.Reflection;
using HB.CustomBuild.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HB.CustomBuild.Tests
{
    /// <summary> 로컬 정리 전 "저장 안 한 에셋" 찾기 (LocalGitClean.FindUnsavedAssets는 internal) </summary>
    public class UnsavedAssetsTests
    {
        private const string Folder = "Assets/__HBBuildUnsavedTest";
        private const string AssetPath = Folder + "/Profile.asset";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__HBBuildUnsavedTest");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<HBBuildProfile>(), AssetPath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        private List<string> FindUnsaved()
        {
            var method = typeof(BuildHelper).Assembly.GetType("HB.CustomBuild.Editor.LocalGitClean")
                .GetMethod("FindUnsavedAssets", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "LocalGitClean.FindUnsavedAssets not found (renamed?)");
            return (List<string>)method.Invoke(null, null);
        }

        [Test]
        public void ModifiedNotSaved_Listed_ThenSaved_NotListed()
        {
            var before = FindUnsaved();
            // 저장 직후에는 이 에셋이 없어야 한다 (항상 dirty로 잡히는 다른 객체가 있으면 로그로 확인)
            CollectionAssert.DoesNotContain(before, AssetPath);
            if (before.Count > 0)
                Debug.Log("[UnsavedAssetsTests] already dirty before change: " + string.Join(", ", before));

            var profile = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(AssetPath);
            profile.Version = "9.9.9";
            EditorUtility.SetDirty(profile);
            CollectionAssert.Contains(FindUnsaved(), AssetPath);

            AssetDatabase.SaveAssets();
            CollectionAssert.DoesNotContain(FindUnsaved(), AssetPath);
        }

        private static bool IsInCleanScope(string assetPath)
        {
            var method = typeof(BuildHelper).Assembly.GetType("HB.CustomBuild.Editor.LocalGitClean")
                .GetMethod("IsInCleanScope", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "LocalGitClean.IsInCleanScope not found (renamed?)");
            string projectPrefix = System.IO.Path.GetFullPath(".").TrimEnd('/', '\\') + System.IO.Path.DirectorySeparatorChar;
            return (bool)method.Invoke(null, new object[] { assetPath, projectPrefix });
        }

        [Test]
        public void CleanScope_ProjectFolders_Included()
        {
            Assert.IsTrue(IsInCleanScope(AssetPath));
            Assert.IsTrue(IsInCleanScope("ProjectSettings/ProjectSettings.asset"));
        }

        [Test]
        public void CleanScope_LibraryAndBuiltin_Excluded()
        {
            Assert.IsFalse(IsInCleanScope("Library/unity default resources"));
            Assert.IsFalse(IsInCleanScope("Resources/unity_builtin_extra"));
            Assert.IsFalse(IsInCleanScope(string.Empty));
        }

        [Test]
        public void CleanScope_ReadOnlyPackage_Excluded()
        {
            // 레지스트리 · 내장 패키지(Library/PackageCache)는 정리가 건드리지 않는다 (URP 셰이더가 열자마자 dirty로 잡히는 문제)
            const string path = "Packages/com.unity.test-framework/package.json";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null || package.source == UnityEditor.PackageManager.PackageSource.Embedded
                                || package.source == UnityEditor.PackageManager.PackageSource.Local)
                Assert.Ignore($"com.unity.test-framework is not a read-only package here ({package?.source})");
            Assert.IsFalse(IsInCleanScope(path));
        }

        [Test]
        public void CleanScope_LocalPackageOutsideProject_Excluded()
        {
            // 이 패키지가 프로젝트 밖 file: 경로로 들어와 있을 때만 (개발 프로젝트에서는 Assets 아래라 해당 없음)
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuildHelper).Assembly);
            if (package == null || package.source != UnityEditor.PackageManager.PackageSource.Local)
                Assert.Ignore("HB_Build is not a local package in this project");
            string root = System.IO.Path.GetFullPath(".").TrimEnd('/', '\\') + System.IO.Path.DirectorySeparatorChar;
            string resolved = System.IO.Path.GetFullPath(package.resolvedPath);
            if (resolved.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                Assert.Ignore("HB_Build local package is inside the project");
            Assert.IsFalse(IsInCleanScope(package.assetPath + "/package.json"));
        }
    }
}
