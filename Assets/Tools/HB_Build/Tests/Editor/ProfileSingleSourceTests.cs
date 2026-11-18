using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HB.CustomBuild.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HB.CustomBuild.Tests
{
    /// <summary> 모든 빌드가 프로필 하나에서 오는지: 배치 인자 · 프로필 → 컨텍스트 · 선택 에셋 슬롯 · Jenkinsfile 생성 </summary>
    public class ProfileSingleSourceTests
    {
        private const string TEMP_DIR = "Assets/HBBuildTests_Temp";

        // 인스턴스 필드 (Unity 7은 정적 필드에 AutoStaticsCleanup 표시를 요구함)
        private readonly FieldInfo m_ArgsField = typeof(BuildHelper).GetField("s_Args", BindingFlags.NonPublic | BindingFlags.Static);
        private object m_SavedArgs;
        private readonly List<Object> m_Created = new List<Object>();
        private readonly List<string> m_CreatedAssets = new List<string>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(m_ArgsField, "BuildHelper.s_Args not found (renamed?)");
            m_SavedArgs = m_ArgsField.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            m_ArgsField.SetValue(null, m_SavedArgs);
            foreach (var o in m_Created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            m_Created.Clear();
            foreach (string path in m_CreatedAssets)
                AssetDatabase.DeleteAsset(path);
            m_CreatedAssets.Clear();
            if (AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.DeleteAsset(TEMP_DIR);
        }

        private void SetArgs(params string[] args)
        {
            m_ArgsField.SetValue(null, new List<string>(args));
        }

        private T New<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            m_Created.Add(o);
            return o;
        }

        private HBBuildProfile NewProfile(ProjectBuild.ESelfBuildPlatform platform = ProjectBuild.ESelfBuildPlatform.PC)
        {
            var p = New<HBBuildProfile>();
            p.name = "TestProfile";
            p.TargetPlatform = platform;
            p.Version = "1.0.0";
            p.VersionCode = "1";
            p.Scenes = new List<string> { "Assets/Scenes/Dummy.unity" };
            return p;
        }

        // ---------- 배치 인자 ----------

        [TestCase("buildType")]
        [TestCase("isAab")]
        [TestCase("zipPackage")]
        [TestCase("deepProfile")]
        [TestCase("cleanBuild")]
        [TestCase("disabledSteps")]
        [TestCase("sceneList")]
        [TestCase("outputPath")]
        [TestCase("autoProfile")]
        public void ValidateArgs_RemovedArg_Fails(string name)
        {
            SetArgs("Unity.exe", "-buildProfile", "Assets/a.asset", "-" + name, "x");

            var ex = Assert.Throws<JenkinsBuildException>(BuildHelper.ValidateArgs);
            StringAssert.Contains("-" + name, ex.Message);
        }

        [Test]
        public void ValidateArgs_ProfileAndThreeOverrides_Pass()
        {
            SetArgs("Unity.exe", "-batchmode", "-buildTarget", "Android", "-buildProfile", "Assets/a.asset",
                "-buildVersion", "1.2.0", "-versionCode", "42", "-devBuild", "true", "-logFile", "build.log");

            Assert.DoesNotThrow(BuildHelper.ValidateArgs);
        }

        [Test]
        public void GetRequiredProfileArg_Missing_Fails()
        {
            SetArgs("Unity.exe", "-buildTarget", "Android", "-buildVersion", "1.0.0");

            Assert.Throws<JenkinsBuildException>(() => BuildHelper.GetRequiredProfileArg());
        }

        [Test]
        public void GetRequiredProfileArg_Present_ReturnsPath()
        {
            SetArgs("Unity.exe", "-buildProfile", "Assets/Editor/HBBuild/Profiles/P.asset");

            Assert.AreEqual("Assets/Editor/HBBuild/Profiles/P.asset", BuildHelper.GetRequiredProfileArg());
        }

        [Test]
        public void ApplyCommandLineOverrides_OnlyVersionVersionCodeDevBuild()
        {
            var profile = NewProfile(ProjectBuild.ESelfBuildPlatform.Android);
            profile.BuildType = EBuildType.QA;
            var context = profile.ToBuildContext();
            SetArgs("Unity.exe", "-buildProfile", "Assets/a.asset", "-buildVersion", "2.0.0", "-versionCode", "77", "-devBuild", "true");

            var apply = typeof(AbsBuild).GetMethod("ApplyCommandLineOverrides", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(apply, "AbsBuild.ApplyCommandLineOverrides not found (renamed?)");
            apply.Invoke(null, new object[] { context });

            Assert.AreEqual("2.0.0", context.Version);
            Assert.AreEqual("77", context.VersionCode);
            Assert.IsTrue(context.IsDevBuild);
            Assert.AreEqual(EBuildType.QA, context.BuildType);
            Assert.IsTrue((context.PlayerOptions.options & BuildOptions.Development) != 0);
        }

        // ---------- 프로필 → 컨텍스트 ----------

        [Test]
        public void ToBuildContext_OutputPath_IsBuildOutputTarget()
        {
            var context = NewProfile(ProjectBuild.ESelfBuildPlatform.Android).ToBuildContext();

            Assert.AreEqual("BuildOutput/Android", context.OutputPath);
        }

        [TestCase("SceneDefine")]
        [TestCase("ProjectDefine")]
        [TestCase("ProvisionUUID")]
        [TestCase("ProvisionType")]
        [TestCase("XcodeSettings")]
        [TestCase("XcodeSettings.Snapshot")]
        public void ToBuildContext_ReservedCustomKey_Fails(string key)
        {
            var profile = NewProfile();
            profile.CustomEntries.Add(new HBBuildProfile.CustomStringEntry { Key = key, Value = "x" });

            Assert.Throws<JenkinsBuildException>(() => profile.ToBuildContext());
        }

        [Test]
        public void ToBuildContext_OtherCustomKey_PassedToSteps()
        {
            var profile = NewProfile();
            profile.CustomEntries.Add(new HBBuildProfile.CustomStringEntry { Key = "MyStepKey", Value = "v" });

            Assert.AreEqual("v", profile.ToBuildContext().GetCustomData<string>("MyStepKey"));
        }

        [Test]
        public void ToBuildContext_ProvisionType_OnlyWithUuidOrAutoSign()
        {
            var profile = NewProfile(ProjectBuild.ESelfBuildPlatform.iOS);

            profile.ProvisionType = EProvisionType.Development;
            Assert.IsFalse(profile.ToBuildContext().CustomData.ContainsKey("ProvisionType"), "no UUID: keep the project's signing");

            profile.ProvisionUUID = " abc ";
            var manual = profile.ToBuildContext();
            Assert.AreEqual("Development", manual.GetCustomData<string>("ProvisionType"));
            Assert.AreEqual("abc", manual.GetCustomData<string>("ProvisionUUID"));

            profile.ProvisionUUID = string.Empty;
            profile.ProvisionType = EProvisionType.AutoSign;
            Assert.AreEqual(PreBuildStep_IOSSettings.PROVISION_TYPE_AUTO_SIGN, profile.ToBuildContext().GetCustomData<string>("ProvisionType"));
        }

        [Test]
        public void ToBuildContext_AutoConnectProfiler_OnlyAddsOptionForDevBuild()
        {
            var profile = NewProfile();
            profile.AutoConnectProfiler = true;

            var release = profile.ToBuildContext();
            Assert.IsTrue(release.AutoConnectProfiler);
            Assert.IsTrue((release.PlayerOptions.options & BuildOptions.ConnectWithProfiler) == 0);

            profile.DevBuild = true;
            Assert.IsTrue((profile.ToBuildContext().PlayerOptions.options & BuildOptions.ConnectWithProfiler) != 0);
        }

        // ---------- 선택 에셋 슬롯 ----------

        [Test]
        public void SettingsSlot_MissingReference_Fails()
        {
            var profile = NewProfile();
            profile.SceneDefineAsset = New<SceneDefine>();
            profile.ProjectDefineAsset = New<ProjectDefine>();
            Object.DestroyImmediate(profile.SceneDefineAsset);
            Object.DestroyImmediate(profile.ProjectDefineAsset);

            var context = profile.ToBuildContext();

            Assert.Throws<JenkinsBuildException>(() => SceneDefine.FromContext(context));
            Assert.Throws<JenkinsBuildException>(() => ProjectDefine.FromContext(context));
        }

        [Test]
        public void SettingsSlot_Assigned_ReturnsAsset()
        {
            var profile = NewProfile();
            var sceneDefine = New<SceneDefine>();
            profile.SceneDefineAsset = sceneDefine;

            Assert.AreSame(sceneDefine, SceneDefine.FromContext(profile.ToBuildContext()));
        }

        [Test]
        public void SettingsSlot_Empty_DoesNotUseDefaultPathAsset()
        {
            if (System.IO.File.Exists(BuildHelper.USER_SCENE_DEFINE_PATH))
                Assert.Ignore($"{BuildHelper.USER_SCENE_DEFINE_PATH} already exists in this project.");

            // 만든 것만 안쪽부터 지운다 (TearDown이 이 순서로 지움)
            bool createdEditorDir = !AssetDatabase.IsValidFolder("Assets/Editor");
            bool createdDir = !AssetDatabase.IsValidFolder(BuildHelper.USER_BUILD_DATA_DIR);
            var onDisk = SceneDefine.CreateAssetInUserFolder();
            m_CreatedAssets.Add(BuildHelper.USER_SCENE_DEFINE_PATH);
            if (createdDir)
                m_CreatedAssets.Add(BuildHelper.USER_BUILD_DATA_DIR);
            if (createdEditorDir)
                m_CreatedAssets.Add("Assets/Editor");
            Assert.IsNotNull(onDisk);

            Assert.IsNull(SceneDefine.FromContext(NewProfile().ToBuildContext()));
        }

        private static void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.CreateFolder("Assets", "HBBuildTests_Temp");
        }

        /// <summary> 프로필을 메모리에서 내리고 디스크에서 다시 읽는다 (디스크에서 읽은 빈 슬롯 · 깨진 참조를 재현) </summary>
        private static HBBuildProfile ReloadFromDisk(HBBuildProfile profile, string path)
        {
            AssetDatabase.SaveAssetIfDirty(profile);
            Resources.UnloadAsset(profile);
            var reloaded = AssetDatabase.LoadAssetAtPath<HBBuildProfile>(path);
            Assert.IsNotNull(reloaded, $"failed to reload {path}");
            return reloaded;
        }

        [Test]
        public void SettingsSlot_EmptyOnDisk_NotPassedToSteps()
        {
            EnsureTempDir();
            string path = TEMP_DIR + "/Profile_EmptySlots.asset";
            var profile = ScriptableObject.CreateInstance<HBBuildProfile>();
            profile.Scenes = new List<string> { "Assets/Scenes/Dummy.unity" };
            AssetDatabase.CreateAsset(profile, path);

            var reloaded = ReloadFromDisk(profile, path);
            var context = reloaded.ToBuildContext();

            Assert.IsFalse(context.CustomData.ContainsKey("SceneDefine"));
            Assert.IsFalse(context.CustomData.ContainsKey("ProjectDefine"));
            Assert.IsFalse(context.CustomData.ContainsKey(BuildStep_XcodeSettings.CUSTOM_DATA_KEY));
            Assert.IsNull(SceneDefine.FromContext(context));
            Assert.IsNull(ProjectDefine.FromContext(context));
        }

        [Test]
        public void SettingsSlot_DeletedAssetOnDisk_FailsAndShowsAsMissing()
        {
            EnsureTempDir();
            string sceneDefinePath = TEMP_DIR + "/SceneDefine_Deleted.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SceneDefine>(), sceneDefinePath);
            string path = TEMP_DIR + "/Profile_DeletedSlot.asset";
            var profile = ScriptableObject.CreateInstance<HBBuildProfile>();
            profile.Scenes = new List<string> { "Assets/Scenes/Dummy.unity" };
            profile.SceneDefineAsset = AssetDatabase.LoadAssetAtPath<SceneDefine>(sceneDefinePath);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.DeleteAsset(sceneDefinePath);

            var reloaded = ReloadFromDisk(profile, path);

            // 빌드: Missing은 건너뛰지 않고 실패
            Assert.Throws<JenkinsBuildException>(() => SceneDefine.FromContext(reloaded.ToBuildContext()));
            // 창: 비어 있음과 Missing을 가르는 조건 (ProjectBuild.DrawOptionalAssetSlot)
            var prop = new SerializedObject(reloaded).FindProperty(nameof(HBBuildProfile.SceneDefineAsset));
            Assert.IsNull(prop.objectReferenceValue);
#if UNITY_7000_0_OR_NEWER
            Assert.AreNotEqual(EntityId.None, prop.objectReferenceEntityIdValue, "a missing reference should keep its id");
#else
            Assert.AreNotEqual(0, prop.objectReferenceInstanceIDValue, "a missing reference should keep its id");
#endif
        }

        // ---------- Jenkinsfile · CLI 명령 ----------

        [Test]
        public void GeneratedCommands_ProfileOnly_NoRemovedArgs()
        {
            EnsureTempDir();
            var profile = ScriptableObject.CreateInstance<HBBuildProfile>();
            profile.TargetPlatform = ProjectBuild.ESelfBuildPlatform.Android;
            string profilePath = TEMP_DIR + "/Profile_Android.asset";
            AssetDatabase.CreateAsset(profile, profilePath);

            var window = ScriptableObject.CreateInstance<ProjectBuild>();
            m_Created.Add(window);
            typeof(ProjectBuild).GetField("m_SelectedProfile", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, profile);

            string local = (string)typeof(ProjectBuild).GetMethod("BuildCommandLine", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, new object[] { false });
            string jenkinsfile = (string)typeof(ProjectBuild).GetMethod("BuildJenkinsfileText", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, null);

            StringAssert.Contains($"-buildProfile \"{profilePath}\"", local);
            StringAssert.Contains("-buildTarget Android", local);
            StringAssert.Contains("-buildProfile \"${profilePath}\"", jenkinsfile);
            StringAssert.Contains($"'{profilePath}': 'Android'", jenkinsfile);
            foreach (string param in new[] { "BUILD_PROFILE", "BUILD_PROFILE_PATH", "BUILD_TARGET", "DEV_BUILD", "BUILD_VERSION", "VERSION_CODE" })
                StringAssert.Contains($"name: '{param}'", jenkinsfile);
            foreach (string removed in BuildHelperRemovedArgs())
            {
                StringAssert.DoesNotContain("-" + removed + " ", local);
                StringAssert.DoesNotContain("-" + removed + " ", jenkinsfile);
            }
            StringAssert.DoesNotContain("name: 'BUILD_TYPE'", jenkinsfile);
            Assert.AreEqual(jenkinsfile.Count(c => c == '{'), jenkinsfile.Count(c => c == '}'), "Groovy braces are unbalanced");
        }

        private static IEnumerable<string> BuildHelperRemovedArgs()
        {
            return (string[])typeof(BuildHelper).GetField("REMOVED_ARGS", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        }
    }
}
