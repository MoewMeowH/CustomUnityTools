using System.IO;
using HB.Cheat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HB.Cheat.Tests
{
    /// <summary>치트 심볼 하나로 합치기: 옛 프로젝트 심볼 정리 · HB_Build DefineContainer 연동 규칙</summary>
    public class CheatDefineMergeTests
    {
        private static readonly string[] s_Add = { "HB_CHEAT" };
        private static readonly string[] s_Remove = { "HB_CHEAT", "HB_CHEAT_HAS_CONSOLE" };

        // ── 옛 심볼 정리 ──

        [Test]
        public void WithoutLegacy_RemovesOnlyWhenCoreIsAlsoOn()
        {
            CollectionAssert.AreEqual(new[] { "A", "HB_CHEAT" }, CheatDefineMigration.WithoutLegacy(new[] { "A", "DEV_CHEAT", "HB_CHEAT" }, "DEV_CHEAT"));
            Assert.IsNull(CheatDefineMigration.WithoutLegacy(new[] { "A", "DEV_CHEAT" }, "DEV_CHEAT"), "HB_CHEAT 없이 쓰는 옛 이름은 치트 밖 용도일 수 있다");
            Assert.IsNull(CheatDefineMigration.WithoutLegacy(new[] { "HB_CHEAT" }, "DEV_CHEAT"));
            Assert.IsNull(CheatDefineMigration.WithoutLegacy(null, "DEV_CHEAT"));
            Assert.IsNull(CheatDefineMigration.WithoutLegacy(new[] { "HB_CHEAT", "DEV_CHEAT" }, null));
        }

        [TestCase("#if DEV_CHEAT", true)]
        [TestCase("  #if DEV_CHEAT && UNITY_EDITOR", true)]
        [TestCase("#elif !DEV_CHEAT", true)]
        [TestCase("# if (DEV_CHEAT)", true)]
        [TestCase("#if DEV_CHEATX", false)]
        [TestCase("#if MY_DEV_CHEAT", false)]
        [TestCase("// #if DEV_CHEAT", false)]
        [TestCase("var s = \"DEV_CHEAT\";", false)]
        [TestCase("#endif // DEV_CHEAT", false)]
        public void IsGuardLine_MatchesWholeSymbolInIfOrElif(string line, bool expected)
        {
            Assert.AreEqual(expected, CheatDefineMigration.IsGuardLine(line, "DEV_CHEAT"));
        }

        [TestCase("[Conditional(\"DEV_CHEAT\")]", true)]
        [TestCase("[System.Diagnostics.Conditional( \"DEV_CHEAT\" )]", true)]
        [TestCase("[ConditionalAttribute(\"DEV_CHEAT\")]", true)]
        [TestCase("[Conditional(\"DEV_CHEATX\")]", false)]
        [TestCase("[Conditional(\"UNITY_EDITOR\")]", false)]
        public void IsConditionalLine_MatchesConditionalAttribute(string line, bool expected)
        {
            Assert.AreEqual(expected, CheatDefineMigration.IsConditionalLine(line, "DEV_CHEAT"));
        }

        [Test]
        public void AsmdefRequires_ChecksDefineConstraintsOnly()
        {
            Assert.IsTrue(CheatDefineMigration.AsmdefRequires("{\"defineConstraints\":[\"HB_CHEAT || DEV_CHEAT\"]}", "DEV_CHEAT"), "식 안의 이름");
            Assert.IsFalse(CheatDefineMigration.AsmdefRequires("{\"defineConstraints\":[\"HB_CHEAT || DEV_CHEATX\"]}", "DEV_CHEAT"));
            Assert.IsTrue(CheatDefineMigration.AsmdefRequires("{\"defineConstraints\":[\"HB_CHEAT\",\" DEV_CHEAT \"]}", "DEV_CHEAT"));
            Assert.IsFalse(CheatDefineMigration.AsmdefRequires("{\"defineConstraints\":[\"!DEV_CHEAT\"]}", "DEV_CHEAT"));
            Assert.IsFalse(CheatDefineMigration.AsmdefRequires("{\"versionDefines\":[{\"define\":\"DEV_CHEAT\"}]}", "DEV_CHEAT"));
            Assert.IsFalse(CheatDefineMigration.AsmdefRequires("not json", "DEV_CHEAT"));
        }

        // ── DefineContainer 규칙 ──

        [TestCase("DEV", "PC", true, false, true)]
        [TestCase("QA", "PC", true, false, true)]
        [TestCase("LIVE", "PC", true, false, null)]
        [TestCase("DEV", "PC", false, true, false)]
        [TestCase("LIVE", "PC", false, true, false)]
        [TestCase("DEV", "MOBILE", false, true, true)]
        [TestCase("QA", "MOBILE", true, false, false)]
        [TestCase("LIVE", "MOBILE", false, false, false)]
        [TestCase("DEV", "NONE", true, true, null)]
        [TestCase(null, "PC", true, true, null)]
        [TestCase("DEV", null, true, true, null)]
        public void WantCheat_FollowsBuildTypeAndPlatformRules(string buildType, string platformType, bool pcOn, bool mobileOn, bool? expected)
        {
            Assert.AreEqual(expected, CheatBuildDefineSync.WantCheat(buildType, platformType, pcOn, mobileOn));
        }

        [Test]
        public void PlatformTypeOf_MapsStandaloneAndMobileOnly()
        {
            Assert.AreEqual("PC", CheatBuildDefineSync.PlatformTypeOf(UnityEditor.Build.NamedBuildTarget.Standalone));
            Assert.AreEqual("MOBILE", CheatBuildDefineSync.PlatformTypeOf(UnityEditor.Build.NamedBuildTarget.Android));
            Assert.AreEqual("MOBILE", CheatBuildDefineSync.PlatformTypeOf(UnityEditor.Build.NamedBuildTarget.iOS));
            Assert.IsNull(CheatBuildDefineSync.PlatformTypeOf(UnityEditor.Build.NamedBuildTarget.WebGL));
            Assert.IsNull(CheatBuildDefineSync.PlatformTypeOf(UnityEditor.Build.NamedBuildTarget.Server));
        }

        [Test]
        public void Apply_AddAppendsMissingAndKeepsOrder()
        {
            CollectionAssert.AreEqual(new[] { "A", "B", "HB_CHEAT" }, CheatBuildDefineSync.Apply(new[] { "A", "B" }, true, s_Add, s_Remove));
            CollectionAssert.AreEqual(new[] { "HB_CHEAT" }, CheatBuildDefineSync.Apply(null, true, s_Add, s_Remove));
            Assert.IsNull(CheatBuildDefineSync.Apply(new[] { "HB_CHEAT", "A" }, true, s_Add, s_Remove));
        }

        [Test]
        public void Apply_RemoveDropsCheatAndConsoleDefines()
        {
            CollectionAssert.AreEqual(new[] { "A", "B" }, CheatBuildDefineSync.Apply(new[] { "A", "HB_CHEAT", "B", "HB_CHEAT_HAS_CONSOLE" }, false, s_Add, s_Remove));
            CollectionAssert.AreEqual(new string[0], CheatBuildDefineSync.Apply(new[] { "HB_CHEAT" }, false, s_Add, s_Remove));
            Assert.IsNull(CheatBuildDefineSync.Apply(new[] { "A" }, false, s_Add, s_Remove));
        }

        // ── 실제 ProjectDefine 에셋 (HB_Build가 있을 때만) ──

        private const string TempFolder = "Assets/__HBCheatMergeTestTmp";

        // 앞 실행이 비정상 종료돼 남은 폴더가 있으면 CreateFolder가 다른 이름을 만든다 → 시작 전에도 지운다
        [SetUp]
        public void SetUp() => DeleteTempFolder();

        [TearDown]
        public void TearDown() => DeleteTempFolder();

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void RewriteEntries_ReadsAndWritesProjectDefineAsset()
        {
            var type = CheatBuildDefineSync.ProjectDefineType;
            if (type == null) Assert.Ignore("HB_Build (ProjectDefine) is not in this project.");

            AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
            string path = TempFolder + "/DefineContainer.asset";
            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, path);

            using (var so = new SerializedObject(asset))
            {
                var list = so.FindProperty("m_DefineContainer");
                list.arraySize = 2;
                SetEntry(list.GetArrayElementAtIndex(0), "DEV", "PC", "A");
                SetEntry(list.GetArrayElementAtIndex(1), "LIVE", "MOBILE", "HB_CHEAT", "B");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 다른 DefineContainer 에셋은 건드리지 않도록 이 경로만 규칙을 적용한다 (켜짐: PC만)
            int changed = CheatBuildDefineSync.RewriteEntries((p, buildType, platformType, defines) =>
            {
                if (p != path) return null;
                var want = CheatBuildDefineSync.WantCheat(buildType, platformType, pcOn: true, mobileOn: false);
                return want.HasValue ? CheatBuildDefineSync.Apply(defines, want.Value, s_Add, s_Remove) : null;
            });

            Assert.AreEqual(2, changed);
            var status = CheatBuildDefineSync.Inspect(pcOn: true, mobileOn: false).FindAll(e => e.AssetPath == path);
            Assert.AreEqual(2, status.Count);
            // 콘솔이 Assets 설치면 HB_CHEAT_HAS_CONSOLE도 있어야 맞는 상태다 (이 시험은 HB_CHEAT만 넣었다)
            bool consoleNeeded = CheatDefines.NeedsManualConsoleDefine();
            Assert.IsTrue(status.Exists(e => e.BuildType == "DEV" && e.PlatformType == "PC" && e.HasCheat && e.IsOutOfSync == consoleNeeded));
            Assert.IsTrue(status.Exists(e => e.BuildType == "LIVE" && e.PlatformType == "MOBILE" && !e.HasCheat && !e.IsOutOfSync && !e.IsLiveWithCheat));

            using (var so = new SerializedObject(AssetDatabase.LoadAssetAtPath(path, type)))
            {
                var defines = so.FindProperty("m_DefineContainer").GetArrayElementAtIndex(0).FindPropertyRelative("DefineArr");
                Assert.AreEqual(2, defines.arraySize);
                Assert.AreEqual("A", defines.GetArrayElementAtIndex(0).stringValue);
                Assert.AreEqual("HB_CHEAT", defines.GetArrayElementAtIndex(1).stringValue);
            }
        }

        private static void SetEntry(SerializedProperty entry, string buildType, string platformType, params string[] defines)
        {
            var bt = entry.FindPropertyRelative("BuildType");
            bt.enumValueIndex = System.Array.IndexOf(bt.enumNames, buildType);
            var pt = entry.FindPropertyRelative("PlatformType");
            pt.enumValueIndex = System.Array.IndexOf(pt.enumNames, platformType);
            var arr = entry.FindPropertyRelative("DefineArr");
            arr.arraySize = defines.Length;
            for (int i = 0; i < defines.Length; i++) arr.GetArrayElementAtIndex(i).stringValue = defines[i];
        }
    }
}
