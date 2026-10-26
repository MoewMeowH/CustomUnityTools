using System;
using System.IO;
using System.Text.RegularExpressions;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper.Tests
{
    public class TempAssetFolderTests
    {
        private const string Root = TempAssetFolder.RootPath;

        [Test]
        public void Nested_KeepsOuterFolder_AndRemovesEmptyRootAfterLast()
        {
            bool rootExisted = AssetDatabase.IsValidFolder(Root);

            var outer = new TempAssetFolder();
            var inner = new TempAssetFolder();
            Assert.IsTrue(AssetDatabase.IsValidFolder(outer.AssetPath));
            Assert.IsTrue(AssetDatabase.IsValidFolder(inner.AssetPath));
            StringAssert.StartsWith(Root + "/", outer.AssetPath);

            inner.Dispose();
            Assert.IsTrue(AssetDatabase.IsValidFolder(outer.AssetPath), "안쪽 폴더를 지울 때 바깥 폴더는 남아야 함");
            Assert.IsFalse(AssetDatabase.IsValidFolder(inner.AssetPath));

            outer.Dispose();
            Assert.IsFalse(AssetDatabase.IsValidFolder(outer.AssetPath));
            if (!rootExisted)
            {
                Assert.IsFalse(AssetDatabase.IsValidFolder(Root));
                Assert.IsFalse(File.Exists(Root + ".meta"));
            }
        }

        [Test]
        public void OwnerMarker_IsWritten_AndNotImported()
        {
            using (var folder = new TempAssetFolder())
            {
                string marker = Path.Combine(folder.AssetPath, TempAssetFolder.MarkerFileName);
                Assert.IsTrue(File.Exists(marker));
                Assert.IsFalse(File.Exists(marker + ".meta"));
            }
        }

        [Test]
        public void FirstUse_DeletesOnlyLeftoversWithTheOwnerMarker()
        {
            using (var keepRoot = new TempAssetFolder())
            {
                string marked = CreateSubFolder("leftover_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(Path.Combine(marked, TempAssetFolder.MarkerFileName), TempAssetFolder.MarkerContent);
                string wrongMarker = CreateSubFolder("wrongmarker_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(Path.Combine(wrongMarker, TempAssetFolder.MarkerFileName), "someone else");
                string unmarked = CreateSubFolder("user_" + Guid.NewGuid().ToString("N"));
                try
                {
                    TempAssetFolder.ResetSweepForTests();
                    LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(marked)));

                    using (new TempAssetFolder()) { }

                    Assert.IsFalse(AssetDatabase.IsValidFolder(marked), "표식이 있는 남은 폴더는 지워져야 함");
                    Assert.IsTrue(AssetDatabase.IsValidFolder(wrongMarker), "표식 내용이 다른 폴더는 남아야 함");
                    Assert.IsTrue(AssetDatabase.IsValidFolder(unmarked), "표식 없는 폴더는 남아야 함");
                    Assert.IsTrue(AssetDatabase.IsValidFolder(keepRoot.AssetPath), "살아 있는 폴더는 남아야 함");
                }
                finally
                {
                    AssetDatabase.DeleteAsset(wrongMarker);
                    AssetDatabase.DeleteAsset(unmarked);
                }
            }
        }

        [Test]
        public void Dispose_Twice_IsSafe()
        {
            var folder = new TempAssetFolder();
            folder.Dispose();
            Assert.DoesNotThrow(folder.Dispose);
        }

        private static string CreateSubFolder(string name)
        {
            string guid = AssetDatabase.CreateFolder(Root, name);
            Assert.IsFalse(string.IsNullOrEmpty(guid));
            return AssetDatabase.GUIDToAssetPath(guid);
        }
    }
}
