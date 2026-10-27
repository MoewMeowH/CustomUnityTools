using System.Reflection;
using HB.CustomBuild.Editor;
using NUnit.Framework;

namespace HB.CustomBuild.Tests
{
    /// <summary> macOS에서 git이 Homebrew git-lfs를 찾게 PATH 뒤에 붙이는 규칙 (GitRunner.AppendMissingPaths는 internal) </summary>
    public class GitRunnerPathTests
    {
        private string Append(string path, params string[] extra)
        {
            var method = typeof(BuildHelper).Assembly.GetType("HB.CustomBuild.Editor.GitRunner")
                .GetMethod("AppendMissingPaths", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "GitRunner.AppendMissingPaths not found (renamed?)");
            return (string)method.Invoke(null, new object[] { path, extra });
        }

        [Test]
        public void AppendsOnlyMissing_KeepsOriginalOrder()
        {
            Assert.AreEqual("/usr/bin:/bin:/usr/local/bin:/opt/homebrew/bin",
                Append("/usr/bin:/bin:/usr/local/bin", "/opt/homebrew/bin", "/usr/local/bin"));
        }

        [Test]
        public void EmptyPath_ExtraOnly()
        {
            Assert.AreEqual("/opt/homebrew/bin:/usr/local/bin", Append(null, "/opt/homebrew/bin", "/usr/local/bin"));
            Assert.AreEqual("/opt/homebrew/bin", Append("::", "/opt/homebrew/bin"));
        }
    }
}
