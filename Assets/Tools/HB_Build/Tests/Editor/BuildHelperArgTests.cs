using System.Collections.Generic;
using System.Reflection;
using HB.CustomBuild.Editor;
using NUnit.Framework;

namespace HB.CustomBuild.Tests
{
    /// <summary> 명령줄 인자 읽기 (Unity가 빈 인자 ""를 버리는 경우 포함) </summary>
    public class BuildHelperArgTests
    {
        private static readonly FieldInfo s_ArgsField = typeof(BuildHelper).GetField("s_Args", BindingFlags.NonPublic | BindingFlags.Static);
        private object m_SavedArgs;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(s_ArgsField, "BuildHelper.s_Args not found (renamed?)");
            m_SavedArgs = s_ArgsField.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            s_ArgsField.SetValue(null, m_SavedArgs);
        }

        private static void SetArgs(params string[] args)
        {
            s_ArgsField.SetValue(null, new List<string>(args));
        }

        [Test]
        public void GetArg_DroppedEmptyValue_NextArgNameIsNotTakenAsValue()
        {
            // Jenkins가 넘긴 -buildVersion "" -versionCode "" -devBuild false 를 Unity가 받은 모양
            SetArgs("Unity.exe", "-buildVersion", "-versionCode", "-devBuild", "false");

            Assert.AreEqual(string.Empty, BuildHelper.GetArg("buildVersion"));
            Assert.AreEqual(string.Empty, BuildHelper.GetArg("versionCode"));
            Assert.AreEqual("false", BuildHelper.GetArg("devBuild"));
        }

        [Test]
        public void GetArg_DroppedEmptyBoolValue_FailsAsMissingValue()
        {
            SetArgs("Unity.exe", "-devBuild", "-cleanBuild", "false");

            Assert.IsTrue(BuildHelper.HasArg("devBuild"));
            Assert.Throws<JenkinsBuildException>(() => BuildHelper.TryGetBoolArg("devBuild", out _));
        }

        [Test]
        public void GetArg_EmptyValueKept_ReturnsEmpty()
        {
            // 빈 인자가 버려지지 않고 남아 들어오는 경우
            SetArgs("Unity.exe", "-buildVersion", "", "-versionCode", "42");

            Assert.AreEqual(string.Empty, BuildHelper.GetArg("buildVersion"));
            Assert.AreEqual("42", BuildHelper.GetArg("versionCode"));
        }

        [Test]
        public void GetArg_NameIsLastToken_ReturnsEmpty()
        {
            SetArgs("Unity.exe", "-buildVersion", "1.0", "-versionCode");

            Assert.IsTrue(BuildHelper.HasArg("versionCode"));
            Assert.AreEqual(string.Empty, BuildHelper.GetArg("versionCode"));
        }

        [Test]
        public void GetArg_NormalValues_Unchanged()
        {
            SetArgs("Unity.exe", "-buildVersion", "1.2.3", "-outputPath", "BuildOutput/StandaloneWindows64", "-sceneList", "NONE");

            Assert.AreEqual("1.2.3", BuildHelper.GetArg("-buildVersion"));
            Assert.AreEqual("BuildOutput/StandaloneWindows64", BuildHelper.GetArg("outputPath"));
            Assert.AreEqual("NONE", BuildHelper.GetArg("sceneList"));
            Assert.AreEqual(string.Empty, BuildHelper.GetArg("versionCode"));
        }
    }
}
