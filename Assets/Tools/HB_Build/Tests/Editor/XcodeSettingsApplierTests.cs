#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HB.CustomBuild.Editor;
using NUnit.Framework;
using UnityEditor.iOS.Xcode;

namespace HB.CustomBuild.Tests
{
    /// <summary>
    /// 실제 Unity가 만든 Xcode 프로젝트 사본(7000.0.0a8, ObjectiveC · Swift 형식)에 적용 (iOS Build Support 모듈 + iOS 활성 플랫폼 필요).
    /// Mac 없이 확인할 수 있는 범위: 생성 파일 · pbxproj · plist 내용.
    /// </summary>
    public class XcodeSettingsApplierTests : XcodeTestFixture
    {
        private string m_Xcode;

        private void UseSample(string format)
        {
            m_Xcode = Path.Combine(TempRoot, "Xcode");
            CopyDirectory(Path.Combine(TestDataRoot, format), m_Xcode);
        }

        private static void CopyDirectory(string from, string to)
        {
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest);
            }
        }

        private List<string> ApplyAll(EBuildType buildType = EBuildType.DEV)
        {
            var input = new XcodePlanInput { BuildType = buildType, ProjectRoot = TempRoot };
            XcodeSettingsApplier.ReadFacts(m_Xcode, input);
            var plan = XcodeSettingsPlanner.Plan(Common, input);
            AssertNoErrors(plan);
            return XcodeSettingsApplier.Apply(plan, m_Xcode, new List<string>());
        }

        private PBXProject ReadProject()
        {
            var proj = new PBXProject();
            proj.ReadFromFile(XcodeSettingsApplier.FindProjectPath(m_Xcode));
            return proj;
        }

        private PlistElementDict ReadPlist(string rel)
        {
            var doc = new PlistDocument();
            doc.ReadFromFile(Path.Combine(m_Xcode, rel));
            return doc.root;
        }

        private string Sha(string rel) => XcodeFileHash.Compute(Path.Combine(m_Xcode, rel));

        [TestCase("ObjC", "Info.plist", "HBXcode.entitlements")]
        [TestCase("Swift", "MainApp/Info.plist", "MainApp/HBXcode.entitlements")]
        public void Apply_WritesInfoEntitlementsSettingsFrameworksFiles(string format, string infoRel, string entRel)
        {
            UseSample(format);
            WriteFile("Loc/ko.lproj/InfoPlist.strings", "\"CFBundleDisplayName\" = \"게임\";");
            WriteFile("Loc/ko.lproj/InfoPlist.strings.meta", "guid: x");
            WriteFile("Firebase/GoogleService-Info.plist", "<plist/>");

            Common.Data.Info.Add(Bool("ITSAppUsesNonExemptEncryption", false));
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "development" });
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.BackgroundModes, Values = new List<string> { "remote-notification" } });
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "OTHER_LDFLAGS", "-lz", EBuildSettingOp.Add));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "HB_TEST_FLAG", "YES"));
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "UserNotifications.framework", Target = EXcodeTarget.UnityFramework, Optional = true });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Firebase/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });

            var log = ApplyAll();
            Assert.IsNotEmpty(log);

            var info = ReadPlist(infoRel);
            Assert.IsFalse(info["ITSAppUsesNonExemptEncryption"].AsBoolean());
            CollectionAssert.Contains(info["UIBackgroundModes"].AsArray().values.Select(v => v.AsString()).ToList(), "remote-notification");
            CollectionAssert.Contains(info["CFBundleLocalizations"].AsArray().values.Select(v => v.AsString()).ToList(), "ko");

            Assert.AreEqual("development", ReadPlist(entRel)["aps-environment"].AsString());

            var proj = ReadProject();
            string app = proj.GetUnityMainTargetGuid();
            string uf = proj.GetUnityFrameworkTargetGuid();
            foreach (var config in proj.BuildConfigNames())
            {
                Assert.AreEqual(entRel, proj.GetBuildPropertyForConfig(proj.BuildConfigByName(app, config), "CODE_SIGN_ENTITLEMENTS"));
                Assert.AreEqual("YES", proj.GetBuildPropertyForConfig(proj.BuildConfigByName(uf, config), "HB_TEST_FLAG"));
            }
            Assert.IsTrue(PbxprojText.TryFindLinked(proj.WriteToString(), proj.GetFrameworksBuildPhaseByTarget(uf), "UserNotifications.framework", out bool weak));
            Assert.IsTrue(weak);
            StringAssert.Contains("-lz", File.ReadAllText(XcodeSettingsApplier.FindProjectPath(m_Xcode)));
            Assert.IsNotNull(proj.FindFileGuidByProjectPath("ko.lproj"));
            Assert.IsNotNull(proj.FindFileGuidByProjectPath("GoogleService-Info.plist"));

            Assert.IsTrue(PbxprojText.PhaseContainsFileRef(proj.WriteToString(), proj.GetResourcesBuildPhaseByTarget(app), proj.FindFileGuidByProjectPath("ko.lproj")));
            Assert.IsTrue(File.Exists(Path.Combine(m_Xcode, "ko.lproj", "InfoPlist.strings")));
            Assert.IsFalse(File.Exists(Path.Combine(m_Xcode, "ko.lproj", "InfoPlist.strings.meta")), ".meta must not be copied into the app bundle");
        }

        [Test]
        public void ExistingDifferentInfoValue_FailsWithoutWriting()
        {
            UseSample("ObjC");
            // Unity 템플릿은 CFBundleDevelopmentRegion을 이미 넣는다
            Common.Data.Info.Add(Str("CFBundleDevelopmentRegion", "zz"));
            Common.Data.Info.Add(Bool("ITSAppUsesNonExemptEncryption", false));
            string infoBefore = Sha("Info.plist");
            string projBefore = Sha("Unity-iPhone.xcodeproj/project.pbxproj");

            var ex = Assert.Throws<InvalidOperationException>(() => ApplyAll());
            StringAssert.Contains("CFBundleDevelopmentRegion", ex.Message);
            Assert.AreEqual(infoBefore, Sha("Info.plist"), "nothing is written when a check fails");
            Assert.AreEqual(projBefore, Sha("Unity-iPhone.xcodeproj/project.pbxproj"));
        }

        [Test]
        public void ExistingDifferentInfoValue_OverwrittenByDecision()
        {
            UseSample("ObjC");
            Common.Data.Info.Add(Str("CFBundleDevelopmentRegion", "ko"));
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "CFBundleDevelopmentRegion", null, memo: "한국어 기본", overwrite: true));

            ApplyAll();
            Assert.AreEqual("ko", ReadPlist("Info.plist")["CFBundleDevelopmentRegion"].AsString());
        }

        /// <summary> UnityFramework Release에 Unity가 이미 넣은 단일 값 설정 하나 (샘플이 바뀌어도 찾도록 후보 중 첫 번째) </summary>
        private (string Key, string Value) ExistingFrameworkSetting()
        {
            var proj = ReadProject();
            string config = proj.BuildConfigByName(proj.GetUnityFrameworkTargetGuid(), "Release");
            foreach (var key in new[] { "SKIP_INSTALL", "DEFINES_MODULE", "MACH_O_TYPE", "ONLY_ACTIVE_ARCH", "SDKROOT", "DEBUG_INFORMATION_FORMAT", "GCC_ENABLE_CPP_EXCEPTIONS" })
            {
                string value = proj.GetBuildPropertyForConfig(config, key);
                if (!string.IsNullOrEmpty(value)) return (key, value);
            }
            Assert.Fail("The sample has none of the candidate settings on UnityFramework/Release.");
            return default;
        }

        [Test]
        public void ExistingDifferentBuildSetting_Fails()
        {
            UseSample("ObjC");
            var (key, existing) = ExistingFrameworkSetting();

            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, key, existing + "_HB", config: "Release"));
            var ex = Assert.Throws<InvalidOperationException>(() => ApplyAll());
            StringAssert.Contains(key, ex.Message);
        }

        [Test]
        public void ExistingSameBuildSettingAndListUnion_Pass()
        {
            UseSample("ObjC");
            var (key, existing) = ExistingFrameworkSetting();

            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, key, existing, config: "Release"));
            Common.Data.Info.Add(Arr("UIRequiredDeviceCapabilities", "arm64"));
            Assert.DoesNotThrow(() => ApplyAll());
        }

        [Test]
        public void ExistingDifferentFile_FailsUnlessOverwrite()
        {
            UseSample("ObjC");
            File.WriteAllText(Path.Combine(m_Xcode, "GoogleService-Info.plist"), "written by another SDK");
            WriteFile("Firebase/GoogleService-Info.plist", "ours");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Firebase/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });

            Assert.Throws<InvalidOperationException>(() => ApplyAll());

            Common.Decisions.Add(Decide(EXcodeChangeKind.File, "GoogleService-Info.plist", null, memo: "우리 파일 우선", overwrite: true));
            ApplyAll();
            Assert.AreEqual("ours", File.ReadAllText(Path.Combine(m_Xcode, "GoogleService-Info.plist")));
        }

        // ───────────── 리뷰 반영 (runId dc15f7d0e5) ─────────────

        private string UnityFrameworkPhase(PBXProject proj) => proj.GetFrameworksBuildPhaseByTarget(proj.GetUnityFrameworkTargetGuid());

        [Test]
        public void ExistingFrameworkWithDifferentWeak_FailsUnlessOverwrite()
        {
            UseSample("ObjC");
            // Unity 7은 AdSupport를 Required로 링크해 둔다
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "AdSupport.framework", Target = EXcodeTarget.UnityFramework, Optional = true });
            var ex = Assert.Throws<InvalidOperationException>(() => ApplyAll());
            StringAssert.Contains("already linked as Required", ex.Message);

            var d = Decide(EXcodeChangeKind.Framework, "AdSupport.framework", null, memo: "iOS 14 미만 대응", overwrite: true);
            d.Target = EXcodeTarget.UnityFramework;
            Common.Decisions.Add(d);
            ApplyAll();
            var proj = ReadProject();
            Assert.IsTrue(PbxprojText.TryFindLinked(proj.WriteToString(), UnityFrameworkPhase(proj), "AdSupport.framework", out bool weak));
            Assert.IsTrue(weak);
        }

        [Test]
        public void ExistingFrameworkSameWeak_Passes()
        {
            UseSample("ObjC");
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "AdSupport.framework", Target = EXcodeTarget.UnityFramework });
            Assert.DoesNotThrow(() => ApplyAll());
        }

        [Test]
        public void Tbd_IsLinkedFromUsrLib()
        {
            UseSample("ObjC");
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "libz.tbd", Target = EXcodeTarget.UnityFramework });
            ApplyAll();
            var proj = ReadProject();
            string text = proj.WriteToString();
            StringAssert.Contains("path = usr/lib/libz.tbd", text);
            Assert.IsTrue(PbxprojText.TryFindLinked(text, UnityFrameworkPhase(proj), "libz.tbd", out _));
        }

        [TestCase("Info.plist", false)]
        [TestCase("HBXcode.entitlements", true)]
        [TestCase("Unity-iPhone.xcodeproj/extra.txt", false)]
        public void Files_CannotOverwriteManagedFiles(string dest, bool withCapability)
        {
            UseSample("ObjC");
            WriteFile("Src/file.txt", "x");
            if (withCapability)
                Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "development" });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src/file.txt", DestinationPath = dest });
            string before = Sha("Info.plist");

            var ex = Assert.Throws<InvalidOperationException>(() => ApplyAll());
            StringAssert.Contains("which Xcode settings edits itself", ex.Message);
            Assert.AreEqual(before, Sha("Info.plist"));
        }

        [Test]
        public void ExistingReferenceWithoutResources_IsAddedToResources()
        {
            UseSample("ObjC");
            // 다른 SDK 후처리기가 파일과 참조만 넣고 Resources에는 안 넣은 상태
            File.WriteAllText(Path.Combine(m_Xcode, "GoogleService-Info.plist"), "same");
            var proj = ReadProject();
            proj.AddFile("GoogleService-Info.plist", "GoogleService-Info.plist");
            proj.WriteToFile(XcodeSettingsApplier.FindProjectPath(m_Xcode));

            WriteFile("Firebase/GoogleService-Info.plist", "same");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Firebase/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });
            var log = ApplyAll();

            proj = ReadProject();
            Assert.IsTrue(PbxprojText.PhaseContainsFileRef(proj.WriteToString(), proj.GetResourcesBuildPhaseByTarget(proj.GetUnityMainTargetGuid()),
                proj.FindFileGuidByProjectPath("GoogleService-Info.plist")));
            Assert.IsTrue(log.Exists(l => l.Contains("already has the same content")));
        }

        [Test]
        public void InfoPlistPathWithVariable_Fails()
        {
            UseSample("ObjC");
            var proj = ReadProject();
            string app = proj.GetUnityMainTargetGuid();
            foreach (var config in proj.BuildConfigNames())
                proj.SetBuildPropertyForConfig(proj.BuildConfigByName(app, config), "INFOPLIST_FILE", "$(SRCROOT)/Info.plist");
            proj.WriteToFile(XcodeSettingsApplier.FindProjectPath(m_Xcode));

            Common.Data.Info.Add(Bool("ITSAppUsesNonExemptEncryption", false));
            var ex = Assert.Throws<InvalidOperationException>(() => ApplyAll());
            StringAssert.Contains("INFOPLIST_FILE", ex.Message);
        }
    }
}
#endif
