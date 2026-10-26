using System.Linq;
using HB.CustomBuild.Editor;
using NUnit.Framework;

namespace HB.CustomBuild.Tests
{
    /// <summary> 공용 + 묶음 합치기 · 충돌 · 결정 · 주인 검사 (iOS 모듈 불필요) </summary>
    public class XcodeSettingsPlannerTests : XcodeTestFixture
    {
        // ───────────── Info · 결정 ─────────────

        [Test]
        public void NullSettings_EmptyPlan()
        {
            var plan = XcodeSettingsPlanner.Plan((HBXcodeSettings)null, new XcodePlanInput());
            Assert.IsFalse(plan.HasChanges);
            Assert.IsEmpty(plan.Errors);
        }

        [Test]
        public void Info_SameValueFromTwoSources_MergedOnce()
        {
            Common.Data.Info.Add(Bool("ITSAppUsesNonExemptEncryption", false));
            AddFragment("SdkA").Data.Info.Add(Bool("ITSAppUsesNonExemptEncryption", false));

            var plan = Plan();
            AssertNoErrors(plan);
            var p = plan.Info.Single();
            Assert.IsFalse(p.Value.BoolValue);
            CollectionAssert.AreEquivalent(new[] { "Common", "SdkA" }, p.Sources);
        }

        [Test]
        public void Info_DifferentScalar_FailsWithBothSources()
        {
            AddFragment("SdkA").Data.Info.Add(Bool("FirebaseAppDelegateProxyEnabled", false));
            AddFragment("SdkB").Data.Info.Add(Bool("FirebaseAppDelegateProxyEnabled", true));

            var plan = Plan();
            AssertError(plan, "values differ");
            AssertError(plan, "SdkA");
            AssertError(plan, "SdkB");
            Assert.IsEmpty(plan.Info);
        }

        [Test]
        public void Info_Conflict_ResolvedByDecision()
        {
            AddFragment("SdkA").Data.Info.Add(Bool("FirebaseAppDelegateProxyEnabled", false));
            var b = AddFragment("SdkB");
            b.Data.Info.Add(Bool("FirebaseAppDelegateProxyEnabled", true));
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "FirebaseAppDelegateProxyEnabled", b));

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.IsTrue(plan.Info.Single().Value.BoolValue);
            Assert.IsEmpty(plan.Warnings);
        }

        [Test]
        public void Decision_WithoutMemo_Fails()
        {
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "X", Common, memo: " "));
            AssertError(Plan(), "needs a memo");
        }

        [Test]
        public void Decision_WithNothingToResolve_Warns()
        {
            Common.Data.Info.Add(Str("A", "1"));
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "A", Common));

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.IsTrue(plan.Warnings.Exists(w => w.Contains("no conflict to resolve")));
        }

        [Test]
        public void Decision_ChoosingDisabledFragment_Fails()
        {
            AddFragment("SdkA").Data.Info.Add(Str("K", "a"));
            var off = AddFragment("Off", enabled: false);
            off.Data.Info.Add(Str("K", "b"));
            Common.Decisions.Add(Decide(EXcodeChangeKind.Info, "K", off));

            AssertError(Plan(), "is not this asset or an enabled fragment");
        }

        [Test]
        public void Snapshot_SurvivesAssetChangesAndDestruction()
        {
            // BuildPlayer가 에셋을 정리해도 빌드 전에 점검한 내용 그대로 적용해야 한다
            var frag = AddFragment("SdkA");
            frag.Data.Info.Add(Str("K", "checked"));
            var snapshot = XcodeSettingsSnapshot.Capture(Common);

            frag.Data.Info[0].Value = "changed later";
            UnityEngine.Object.DestroyImmediate(frag);
            UnityEngine.Object.DestroyImmediate(Common);

            var plan = XcodeSettingsPlanner.Plan(snapshot, new XcodePlanInput { ProjectRoot = TempRoot });
            AssertNoErrors(plan);
            Assert.AreEqual("checked", plan.Info.Single().Value.Text);
            Assert.AreEqual("SdkA", plan.Info.Single().Sources.Single());
        }

        [Test]
        public void Info_TypeMismatch_Fails()
        {
            AddFragment("SdkA").Data.Info.Add(Str("Key", "true"));
            AddFragment("SdkB").Data.Info.Add(Bool("Key", true));
            AssertError(Plan(), "value types differ");
        }

        [Test]
        public void Info_StringArray_Union()
        {
            AddFragment("SdkA").Data.Info.Add(Arr("LSApplicationQueriesSchemes", "fb", "kakao"));
            AddFragment("SdkB").Data.Info.Add(Arr("LSApplicationQueriesSchemes", "kakao", "line"));

            var plan = Plan();
            AssertNoErrors(plan);
            CollectionAssert.AreEqual(new[] { "fb", "kakao", "line" }, plan.Info.Single().Value.Items.Select(i => i.Text));
        }

        [Test]
        public void SkAdNetworkItems_UnionByIdentifier_IgnoringCase()
        {
            const string a = "<array><dict><key>SKAdNetworkIdentifier</key><string>aaa.skadnetwork</string></dict>" +
                             "<dict><key>SKAdNetworkIdentifier</key><string>bbb.skadnetwork</string></dict></array>";
            const string b = "<array><dict><key>SKAdNetworkIdentifier</key><string>BBB.skadnetwork</string></dict>" +
                             "<dict><key>SKAdNetworkIdentifier</key><string>ccc.skadnetwork</string></dict></array>";
            AddFragment("SdkA").Data.Info.Add(Raw("SKAdNetworkItems", a));
            AddFragment("SdkB").Data.Info.Add(Raw("SKAdNetworkItems", b));

            var plan = Plan();
            AssertNoErrors(plan);
            var ids = plan.Info.Single().Value.Items.Select(i => i.Get("SKAdNetworkIdentifier").Text).ToList();
            CollectionAssert.AreEqual(new[] { "aaa.skadnetwork", "bbb.skadnetwork", "ccc.skadnetwork" }, ids);
        }

        [Test]
        public void SkAdNetworkItems_WrongShape_Fails()
        {
            Common.Data.Info.Add(Raw("SKAdNetworkItems", "<array><string>x</string></array>"));
            AssertError(Plan(), "SKAdNetworkIdentifier");
        }

        [Test]
        public void OwnedInfoKey_Fails()
        {
            Common.Data.Info.Add(Str("CFBundleVersion", "3"));
            AssertError(Plan(), "owned by HB_Build build profile");
        }

        [Test]
        public void InvalidValues_Fail()
        {
            Common.Data.Info.Add(new XcodePlistEntry { Key = "I", Type = EPlistValueType.Integer, Value = "1.5" });
            Common.Data.Info.Add(new XcodePlistEntry { Key = "B", Type = EPlistValueType.Bool, Value = "yes" });
            Common.Data.Info.Add(new XcodePlistEntry { Key = "R", Type = EPlistValueType.Raw, RawXml = "<array>" });
            Common.Data.Info.Add(new XcodePlistEntry { Key = " ", Type = EPlistValueType.String });

            var plan = Plan();
            AssertError(plan, "'I': Integer value is invalid");
            AssertError(plan, "'B': Bool value must be true or false");
            AssertError(plan, "'R': Invalid plist XML");
            AssertError(plan, "empty key");
        }

        [Test]
        public void BuildTypes_FilterRows()
        {
            Common.Data.Info.Add(new XcodePlistEntry { Key = "LiveOnly", Value = "x", BuildTypes = EXcodeBuildTypes.LIVE });
            Common.Data.Info.Add(new XcodePlistEntry { Key = "Never", Value = "x", BuildTypes = 0 });

            var dev = Plan(EBuildType.DEV);
            Assert.IsEmpty(dev.Info);
            Assert.IsTrue(dev.Warnings.Exists(w => w.Contains("'Never'") && w.Contains("no build type")));
            Assert.AreEqual("LiveOnly", Plan(EBuildType.LIVE).Info.Single().Key);
        }

        [Test]
        public void Fragments_DisabledSkipped_EmptyAndDuplicateWarn()
        {
            AddFragment("Off", enabled: false).Data.Info.Add(Str("OffKey", "x"));
            Common.Fragments.Add(new XcodeFragmentRef { Enabled = true, Fragment = null });
            var dup = AddFragment("Dup");
            dup.Data.Info.Add(Str("DupKey", "x"));
            Common.Fragments.Add(new XcodeFragmentRef { Enabled = true, Fragment = dup });

            var plan = Plan();
            AssertNoErrors(plan);
            CollectionAssert.AreEqual(new[] { "DupKey" }, plan.Info.Select(i => i.Key));
            Assert.IsTrue(plan.Warnings.Exists(w => w.Contains("enabled but empty")));
            Assert.IsTrue(plan.Warnings.Exists(w => w.Contains("more than once")));
        }

        // ───────────── Capabilities ─────────────

        [Test]
        public void Capability_Push_ConflictsWithDirectEntitlementRow()
        {
            AddFragment("PushSdk").Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "production" });
            Common.Data.Entitlements.Add(Str("aps-environment", "development"));

            AssertError(Plan(), "Entitlement 'aps-environment': values differ");
        }

        [Test]
        public void Capability_BackgroundModes_MergesWithInfoRow()
        {
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry
            {
                Capability = EXcodeCapability.BackgroundModes, Values = new System.Collections.Generic.List<string> { "remote-notification" },
            });
            AddFragment("AudioSdk").Data.Info.Add(Arr("UIBackgroundModes", "audio"));

            var plan = Plan();
            AssertNoErrors(plan);
            CollectionAssert.AreEqual(new[] { "remote-notification", "audio" }, plan.Info.Single().Value.Items.Select(i => i.Text));
            CollectionAssert.AreEqual(new[] { EXcodeCapability.BackgroundModes }, plan.Capabilities);
        }

        [Test]
        public void Capability_ValuesRequired_And_ApsValidated()
        {
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.AssociatedDomains });
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "prod" });

            var plan = Plan();
            AssertError(plan, "needs at least one value");
            AssertError(plan, "must be 'development' or 'production'");
        }

        [Test]
        public void ApsEnvironment_MismatchWithProvisionType_Warns()
        {
            Common.Data.Capabilities.Add(new XcodeCapabilityEntry { Capability = EXcodeCapability.PushNotifications, ApsEnvironment = "development" });

            Assert.IsTrue(Plan(provisionType: "Distribution").Warnings.Exists(w => w.Contains("aps-environment")));
            Assert.IsFalse(Plan(provisionType: "Development").Warnings.Exists(w => w.Contains("aps-environment")));
            Assert.IsFalse(Plan(provisionType: "AutoSign").Warnings.Exists(w => w.Contains("aps-environment")));
        }

        // ───────────── Build Settings · Frameworks ─────────────

        [Test]
        public void BuildSetting_AllVsSpecificTarget_Conflicts()
        {
            AddFragment("SdkA").Data.BuildSettings.Add(Setting(EXcodeTarget.All, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES"));
            AddFragment("SdkB").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO"));

            AssertError(Plan(), "[UnityFramework/Release] 'ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES': values differ");
        }

        [Test]
        public void BuildSetting_DifferentTargets_NoConflict()
        {
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO"));

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.AreEqual(8, plan.BuildSettings.Count); // 2 타깃 × 4 구성
        }

        [Test]
        public void BuildSetting_DecisionPerTargetResolves()
        {
            AddFragment("SdkA").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "SWIFT_VERSION", "5.0"));
            var b = AddFragment("SdkB");
            b.Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "SWIFT_VERSION", "5.9"));
            var d = Decide(EXcodeChangeKind.BuildSetting, "SWIFT_VERSION", b);
            d.Target = EXcodeTarget.UnityFramework;
            Common.Decisions.Add(d);

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.IsTrue(plan.BuildSettings.All(s => s.Values.Single() == "5.9"));
        }

        [Test]
        public void BuildSetting_SetAndAddMixed_Fails()
        {
            AddFragment("SdkA").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "OTHER_LDFLAGS", "$(inherited) -ObjC"));
            AddFragment("SdkB").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "OTHER_LDFLAGS", "-lz", EBuildSettingOp.Add));
            AssertError(Plan(), "Set and Add are mixed");
        }

        [Test]
        public void BuildSetting_AddUnion_And_ListSetWarning()
        {
            AddFragment("SdkA").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "OTHER_LDFLAGS", "-ObjC", EBuildSettingOp.Add, "Release"));
            AddFragment("SdkB").Data.BuildSettings.Add(Setting(EXcodeTarget.UnityFramework, "OTHER_LDFLAGS", "-lz", EBuildSettingOp.Add, "Release"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks", config: "Release"));

            var plan = Plan();
            AssertNoErrors(plan);
            var ld = plan.BuildSettings.Single(s => s.Key == "OTHER_LDFLAGS");
            CollectionAssert.AreEqual(new[] { "-ObjC", "-lz" }, ld.Values);
            Assert.IsTrue(plan.Warnings.Exists(w => w.Contains("LD_RUNPATH_SEARCH_PATHS") && w.Contains("$(inherited)")));
        }

        [Test]
        public void BuildSetting_Owned_UnknownConfig_MissingGameAssembly_Fail()
        {
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "CODE_SIGN_IDENTITY[sdk=iphoneos*]", "Apple Distribution"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.App, "X", "1", config: "Staging"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.GameAssembly, "Y", "1"));
            Common.Data.BuildSettings.Add(Setting(EXcodeTarget.All, "Z", "1"));

            var plan = XcodeSettingsPlanner.Plan(Common, new XcodePlanInput { ProjectRoot = TempRoot, HasGameAssembly = false });
            AssertError(plan, "owned by HB_Build build profile");
            AssertError(plan, "config 'Staging' does not exist");
            AssertError(plan, "no GameAssembly target");
            Assert.IsFalse(plan.BuildSettings.Any(s => s.Key == "Z" && s.Target == EXcodeTarget.GameAssembly));
            Assert.AreEqual(8, plan.BuildSettings.Count(s => s.Key == "Z"));
        }

        [Test]
        public void Framework_RequiredOptionalMixed_And_BadName_Fail()
        {
            AddFragment("SdkA").Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "AdSupport.framework", Optional = false });
            AddFragment("SdkB").Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "AdSupport.framework", Optional = true });
            Common.Data.Frameworks.Add(new XcodeFrameworkEntry { Name = "AdSupport" });

            var plan = Plan();
            AssertError(plan, "Required and Optional are mixed");
            AssertError(plan, "must end with .framework or .tbd");
        }

        // ───────────── Files ─────────────

        [Test]
        public void Files_InvalidDestinationsAndSources_Fail()
        {
            WriteFile("Src/a.txt", "a");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src/a.txt", DestinationPath = "../a.txt" });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src/a.txt", DestinationPath = "/abs/a.txt" });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src/missing.txt", DestinationPath = "m.txt" });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src", DestinationPath = "dir", Mode = EXcodeFileMode.File });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Src/a.txt", DestinationPath = "ga.txt", Target = EXcodeTarget.GameAssembly });

            var plan = Plan();
            AssertError(plan, "must not contain '.', '..'");
            AssertError(plan, "must be relative to the Xcode project folder");
            AssertError(plan, "'Src/missing.txt' does not exist");
            AssertError(plan, "use FolderReference");
            AssertError(plan, "only be added to App or UnityFramework");
        }

        [Test]
        public void Files_SameDestination_SameContentMerged_DifferentContentFails()
        {
            WriteFile("A/GoogleService-Info.plist", "same");
            WriteFile("B/GoogleService-Info.plist", "same");
            WriteFile("C/GoogleService-Info.plist", "other");
            AddFragment("SdkA").Data.Files.Add(new XcodeFileEntry { SourcePath = "A/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });
            AddFragment("SdkB").Data.Files.Add(new XcodeFileEntry { SourcePath = "B/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist" });

            var plan = Plan();
            AssertNoErrors(plan);
            Assert.AreEqual(1, plan.Files.Count);

            AddFragment("SdkC").Data.Files.Add(new XcodeFileEntry { SourcePath = "C/GoogleService-Info.plist", DestinationPath = "googleservice-info.plist" });
            AssertError(Plan(), "different contents or settings for the same destination");
        }

        [Test]
        public void Files_BuildTypeSelectsSource()
        {
            WriteFile("Dev/GoogleService-Info.plist", "dev");
            WriteFile("Live/GoogleService-Info.plist", "live");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Dev/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist", BuildTypes = EXcodeBuildTypes.DEV | EXcodeBuildTypes.QA });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Live/GoogleService-Info.plist", DestinationPath = "GoogleService-Info.plist", BuildTypes = EXcodeBuildTypes.LIVE });

            StringAssert.Contains("Dev", Plan(EBuildType.QA).Files.Single().SourceFullPath);
            StringAssert.Contains("Live", Plan(EBuildType.LIVE).Files.Single().SourceFullPath);
        }

        [Test]
        public void Files_Lproj_FillsLocalizations_FolderHashIgnoresMeta()
        {
            WriteFile("Loc/ko.lproj/InfoPlist.strings", "\"CFBundleDisplayName\" = \"게임\";");
            WriteFile("Loc/ja.lproj/InfoPlist.strings", "\"CFBundleDisplayName\" = \"ゲーム\";");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ja.lproj", DestinationPath = "ja.lproj", Mode = EXcodeFileMode.FolderReference });

            var plan = Plan();
            AssertNoErrors(plan);
            CollectionAssert.AreEqual(new[] { "ko", "ja" }, plan.Info.Single(i => i.Key == "CFBundleLocalizations").Value.Items.Select(i => i.Text));

            string before = plan.Files[0].Hash;
            WriteFile("Loc/ko.lproj/InfoPlist.strings.meta", "guid: 123");
            Assert.AreEqual(before, Plan().Files[0].Hash);

            Common.FillLocalizationsFromLproj = false;
            Assert.IsFalse(Plan().Info.Any(i => i.Key == "CFBundleLocalizations"));
        }

        [Test]
        public void Files_NestedDestinations_Fail()
        {
            WriteFile("Loc/ko.lproj/InfoPlist.strings", "x");
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ko.lproj", DestinationPath = "ko.lproj", Mode = EXcodeFileMode.FolderReference });
            Common.Data.Files.Add(new XcodeFileEntry { SourcePath = "Loc/ko.lproj/InfoPlist.strings", DestinationPath = "ko.lproj/InfoPlist.strings" });
            AssertError(Plan(), "is inside another destination");
        }
    }
}
