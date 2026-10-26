using System.Collections.Generic;
using System.IO;
using HB.CustomBuild.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HB.CustomBuild.Tests
{
    /// <summary> Xcode 설정 테스트 공용: 메모리 에셋 · 임시 폴더(Unity Temp 아님 — 에디터가 닫히면 지워짐) </summary>
    public abstract class XcodeTestFixture
    {
        protected HBXcodeSettings Common;
        protected string TempRoot;
        private readonly List<Object> m_Created = new List<Object>();

        [SetUp]
        public void BaseSetUp()
        {
            Common = NewSettings("Common");
            TempRoot = Path.Combine(Path.GetTempPath(), "HBXcodeTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempRoot);
        }

        [TearDown]
        public void BaseTearDown()
        {
            foreach (var o in m_Created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            m_Created.Clear();
            if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, true);
        }

        /// <summary> Unity 7000.0.0a8이 만든 Xcode 프로젝트 사본 (ObjC · Swift) </summary>
        protected static string TestDataRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(XcodeTestFixture).Assembly);
                string root = info != null ? info.resolvedPath : Path.GetFullPath("Assets/Tools/HB_Build");
                return Path.Combine(root, "Tests", "Editor", "TestData~");
            }
        }

        protected HBXcodeSettings NewSettings(string name)
        {
            var s = ScriptableObject.CreateInstance<HBXcodeSettings>();
            s.name = name;
            m_Created.Add(s);
            return s;
        }

        protected HBXcodeFragment AddFragment(string name, bool enabled = true)
        {
            var f = ScriptableObject.CreateInstance<HBXcodeFragment>();
            f.name = name;
            m_Created.Add(f);
            Common.Fragments.Add(new XcodeFragmentRef { Enabled = enabled, Fragment = f });
            return f;
        }

        protected XcodePlan Plan(EBuildType buildType = EBuildType.DEV, string provisionType = null) =>
            XcodeSettingsPlanner.Plan(Common, new XcodePlanInput { BuildType = buildType, ProjectRoot = TempRoot, ProvisionType = provisionType });

        protected static XcodePlistEntry Str(string key, string value) => new XcodePlistEntry { Key = key, Type = EPlistValueType.String, Value = value };
        protected static XcodePlistEntry Bool(string key, bool value) => new XcodePlistEntry { Key = key, Type = EPlistValueType.Bool, Value = value ? "true" : "false" };
        protected static XcodePlistEntry Arr(string key, params string[] values) => new XcodePlistEntry { Key = key, Type = EPlistValueType.StringArray, Values = new List<string>(values) };
        protected static XcodePlistEntry Raw(string key, string xml) => new XcodePlistEntry { Key = key, Type = EPlistValueType.Raw, RawXml = xml };

        protected static XcodeBuildSettingEntry Setting(EXcodeTarget target, string key, string value, EBuildSettingOp op = EBuildSettingOp.Set, string config = "") =>
            new XcodeBuildSettingEntry { Target = target, Key = key, Value = value, Op = op, Config = config };

        protected static XcodeDecisionEntry Decide(EXcodeChangeKind kind, string key, Object chosen, string memo = "test", bool overwrite = false) =>
            new XcodeDecisionEntry { Kind = kind, Key = key, ChosenSource = chosen, Memo = memo, OverwriteExisting = overwrite };

        /// <summary> TempRoot 기준 파일을 만들고 상대 경로를 돌려준다 </summary>
        protected string WriteFile(string relPath, string content)
        {
            string full = Path.Combine(TempRoot, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return relPath;
        }

        protected static void AssertNoErrors(XcodePlan plan) =>
            Assert.IsEmpty(plan.Errors, "Errors:\n" + string.Join("\n", plan.Errors));

        protected static void AssertError(XcodePlan plan, string contains) =>
            Assert.IsTrue(plan.Errors.Exists(e => e.Contains(contains)), $"Expected an error containing '{contains}'. Errors:\n{string.Join("\n", plan.Errors)}");
    }
}
