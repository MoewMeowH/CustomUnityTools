using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HB.CustomBuild.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HB.CustomBuild.Tests
{
    /// <summary> 2026-10-26 교차 리뷰(runId dc15f7d0e5) 반영분 — iOS 모듈 불필요 </summary>
    public class XcodeReviewFixTests : XcodeTestFixture
    {
        // ───────────── Files 원본 · 대상 ─────────────

        [Test]
        public void Files_SourceOutsideProject_Fails()
        {
            string outside = Path.Combine(Path.GetDirectoryName(TempRoot), "HBXcodeOutside_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                File.WriteAllText(Path.Combine(outside, "secret.txt"), "x");
                Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "../" + Path.GetFileName(outside) + "/secret.txt", DestinationPath = "secret.txt" });
                AssertError(Plan(), "is outside the Unity project");
            }
            finally
            {
                Directory.Delete(outside, true);
            }
        }

        [Test]
        public void Files_SensitiveFiles_Fail()
        {
            WriteFile("Live/GoogleService-Info.plist", "x");
            WriteFile("Live/AuthKey_ABC.p8", "key");
            WriteFile("Keys/dist.p12", "cert");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Live", DestinationPath = "Live", Mode = EXcodeFileMode.FolderReference });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Keys/dist.p12", DestinationPath = "dist.p12" });

            var plan = Plan();
            AssertError(plan, "'AuthKey_ABC.p8' must not be copied");
            AssertError(plan, "'dist.p12' must not be copied");
        }

        [Test]
        public void Files_HiddenFilesAreNotPartOfFolder()
        {
            WriteFile("Loc/ko.lproj/InfoPlist.strings", "x");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference });
            string before = Plan().Files.Single().Hash;

            WriteFile("Loc/ko.lproj/.DS_Store", "finder");
            WriteFile("Loc/ko.lproj/.git/config", "x");
            Assert.AreEqual(before, Plan().Files.Single().Hash);
            CollectionAssert.AreEqual(new[] { "InfoPlist.strings" }, XcodeFileHash.ListFolderFiles(Path.Combine(TempRoot, "Loc/ko.lproj")));
        }

        [TestCase("NUL")]
        [TestCase("x/CON.txt")]
        [TestCase("dir./a.txt")]
        [TestCase("dir /a.txt")]
        public void Destination_DeviceNamesAndTrailingDots_Fail(string dest)
        {
            Assert.IsNull(XcodeSettingsPlanner.NormalizeDestination(dest, out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void FileDecisionKey_IsNormalized()
        {
            WriteFile("A/GoogleService-Info.plist", "a");
            WriteFile("B/GoogleService-Info.plist", "b");
            AddFragment("SdkA").Data.Files.Add(new XcodeFileEntry { SourcePath = "A/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });
            var b = AddFragment("SdkB");
            b.Data.Files.Add(new XcodeFileEntry { SourcePath = "B/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });
            Common.Decisions.Add(Decide(EXcodeChangeKind.File, "/GoogleService-Info.plist/", b));

            var plan = Plan();
            AssertNoErrors(plan);
            StringAssert.Contains(Path.Combine("B", "GoogleService-Info.plist"), plan.Files.Single().SourceFullPath);
        }

        [Test]
        public void Localizations_ComeFromFinalFilesOnly()
        {
            WriteFile("A/ko.lproj/InfoPlist.strings", "a");
            WriteFile("B/ko.lproj/InfoPlist.strings", "b");
            AddFragment("SdkA").Data.Files.Add(new XcodeFileEntry { SourcePath = "A/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference });
            var b = AddFragment("SdkB");
            b.Data.Files.Add(new XcodeFileEntry { SourcePath = "B/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference, AddToResources = false });
            Common.Decisions.Add(Decide(EXcodeChangeKind.File, "ko.lproj", b));

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.IsFalse(plan.Info.Any(i => i.Key == "CFBundleLocalizations"), "the losing App/Resources source must not add a localization");
        }

        // ───────────── 값 · 결정 · 주인 ─────────────

        [Test]
        public void Real_FloatPrecisionAndRange()
        {
            Common.Data.Info.Add(new XcodePlistEntry { Key = "R", Type = EPlistValueType.Real, Value = "0.1" });
            var plan = Plan();
            AssertNoErrors(plan);
            Assert.AreEqual((double)0.1f, plan.Info.Single().Value.RealValue);

            Common.Data.Info.Add(new XcodePlistEntry { Key = "Big", Type = EPlistValueType.Real, Value = "1e100" });
            Common.Data.Info.Add(Raw("Nested", "<dict><key>x</key><real>1e100</real></dict>"));
            plan = Plan();
            AssertError(plan, "'Big': Real value");
            AssertError(plan, "'Nested': Real value");
        }

        [Test]
        public void Decision_ChoiceWithOverwrite_IsNotReportedAsLeftover()
        {
            Common.Data.Info.Add(Str("CFBundleDevelopmentRegion", "ko"));
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "CFBundleDevelopmentRegion", Common, overwrite: true));

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.IsFalse(plan.Warnings.Exists(w => w.Contains("no conflict to resolve")));
            Assert.AreEqual(1, plan.OverwriteDecisions.Count);
        }

        [Test]
        public void MissingFragment_LiveFails_DevWarns()
        {
            Common.Fragments.Add(new XcodeFragmentRef { Enabled = true, Fragment = null });
            var gone = AddFragment("Gone");
            UnityEngine.Object.DestroyImmediate(gone);

            var dev = Plan(EBuildType.DEV);
            AssertNoErrors(dev);
            Assert.IsTrue(dev.Warnings.Exists(w => w.Contains("enabled but empty")));
            Assert.IsTrue(dev.Warnings.Exists(w => w.Contains("missing fragment asset")));

            var live = Plan(EBuildType.LIVE);
            AssertError(live, "enabled but empty");
            AssertError(live, "missing fragment asset");
        }

        [Test]
        public void Owners_CoverSigningAndInfoGenerationKeys()
        {
            Common.Data.Entitlements.Add(Bool("get-task-allow", true));
            Common.Data.Info.Add(Bool("UIRequiresPersistentWiFi", true));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "INFOPLIST_KEY_CFBundleDisplayName", "X"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "OTHER_CODE_SIGN_FLAGS", "--deep"));

            var plan = Plan();
            AssertError(plan, "'get-task-allow': this key is owned by code signing");
            AssertError(plan, "'UIRequiresPersistentWiFi': this key is owned by PlayerSettings");
            AssertError(plan, "'INFOPLIST_KEY_CFBundleDisplayName': this build setting is owned by the Info tab");
            AssertError(plan, "'OTHER_CODE_SIGN_FLAGS': this build setting is owned by HB_Build build profile");
        }

        [Test]
        public void Live_WarnsOnInsecureValues()
        {
            Common.Data.Info.Add(Raw("NSAppTransportSecurity",
                "<dict><key>NSAllowsArbitraryLoads</key><true/><key>NSExceptionDomains</key><dict><key>example.com</key><dict><key>NSExceptionAllowsInsecureHTTPLoads</key><true/></dict></dict></dict>"));
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "development" });

            var live = Plan(EBuildType.LIVE);
            AssertNoErrors(live);
            Assert.IsTrue(live.Warnings.Exists(w => w.Contains("NSAllowsArbitraryLoads")));
            Assert.IsTrue(live.Warnings.Exists(w => w.Contains("example.com")));
            Assert.IsTrue(live.Warnings.Exists(w => w.Contains("aps-environment 'development'")));
            Assert.IsFalse(Plan(EBuildType.DEV).Warnings.Exists(w => w.Contains("LIVE build")));
        }

        [Test]
        public void Tbd_CannotBeOptional()
        {
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "libz.tbd", Optional = true });
            AssertError(Plan(), "only supported for .framework");
        }

        // ───────────── pbxproj 텍스트 ─────────────

        [Test]
        public void PbxprojText_ReadsLinkedFrameworksAndWeakFlag()
        {
            string pbx = File.ReadAllText(Path.Combine(TestDataRoot, "ObjC", "Unity-iPhone.xcodeproj", "project.pbxproj"));
            const string unityFrameworkFrameworksPhase = "9D25AB99213FB47800354C27";

            Assert.IsTrue(PbxprojText.TryFindLinked(pbx, unityFrameworkFrameworksPhase, "AdSupport.framework", out bool weak));
            Assert.IsFalse(weak, "Unity links AdSupport as Required in the 7000.0.0a8 sample");
            Assert.IsTrue(PbxprojText.TryFindLinked(pbx, unityFrameworkFrameworksPhase, "libiconv.2.tbd", out _));
            Assert.IsFalse(PbxprojText.TryFindLinked(pbx, unityFrameworkFrameworksPhase, "NotThere.framework", out _));

            string weakLine = pbx.Replace("fileRef = 65F37FDAB09705DF055776EF /* AdSupport.framework */; };",
                                          "fileRef = 65F37FDAB09705DF055776EF /* AdSupport.framework */; settings = {ATTRIBUTES = (Weak, ); }; };");
            Assert.IsTrue(PbxprojText.TryFindLinked(weakLine, unityFrameworkFrameworksPhase, "AdSupport.framework", out weak));
            Assert.IsTrue(weak);
        }

        // ───────────── 스텝 · 인스펙터 ─────────────

        private static BuildContext IosContext() => new BuildContext { Target = BuildTarget.iOS, TargetGroup = BuildTargetGroup.iOS, BuildType = EBuildType.DEV };

        [Test]
        public void Step_CustomDataWithWrongType_Fails()
        {
            var ctx = IosContext();
            ctx.SetCustomData(BuildStep_XcodeSettings.CUSTOM_DATA_KEY, "Assets/Editor/HBBuild/XcodeSettings.asset");
            var ex = Assert.Catch<Exception>(() => new BuildStep_XcodeSettings().OnPreBuild(ctx));
            StringAssert.Contains("not an HBXcodeSettings", ex.Message);
        }

        [Test]
        public void Step_MissingReference_Fails()
        {
            var gone = NewSettings("Gone");
            UnityEngine.Object.DestroyImmediate(gone);
            var ctx = IosContext();
            ctx.SetCustomData(BuildStep_XcodeSettings.CUSTOM_DATA_KEY, gone);
            var ex = Assert.Catch<Exception>(() => new BuildStep_XcodeSettings().OnPreBuild(ctx));
            StringAssert.Contains("missing", ex.Message);
        }

        [Test]
        public void NewInspectorRow_GetsDefaults_ExistingRowUntouched()
        {
            var so = new SerializedObject(Common);
            var list = so.FindProperty("Data.Info");
            list.arraySize = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            TestContext.WriteLine($"Unity new list element: Enabled={Common.Data.Info[0].Enabled}, BuildTypes={Common.Data.Info[0].BuildTypes}");

            so.Update();
            XcodeRowDefaults.EnsureInitialized(list.GetArrayElementAtIndex(0));
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(Common.Data.Info[0].Enabled);
            Assert.AreEqual(EXcodeBuildTypes.All, Common.Data.Info[0].BuildTypes);

            // 사용자가 끈 행은 다시 켜지 않는다
            Common.Data.Info[0].Enabled = false;
            so.Update();
            XcodeRowDefaults.EnsureInitialized(list.GetArrayElementAtIndex(0));
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(Common.Data.Info[0].Enabled);

            var decisions = so.FindProperty("Decisions");
            decisions.arraySize = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();
            XcodeRowDefaults.EnsureInitialized(decisions.GetArrayElementAtIndex(0));
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(EXcodeTarget.All, Common.Decisions[0].Target);
        }
    }
}
