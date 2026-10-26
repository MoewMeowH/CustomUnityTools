using System.IO;
using HB.TestFrameworkHelper.Editor;
using NUnit.Framework;

namespace HB.TestFrameworkHelper.Tests
{
    public class TempDirectoryTests
    {
        [Test]
        public void Dispose_DeletesTree_IncludingReadOnlyFiles()
        {
            var dir = new TempDirectory();
            Assert.IsTrue(Directory.Exists(dir.FullPath));
            StringAssert.StartsWith(Path.GetTempPath(), dir.FullPath);

            string nested = Path.Combine(dir.FullPath, "a", "b");
            Directory.CreateDirectory(nested);
            string readOnly = Path.Combine(nested, "ro.txt");
            File.WriteAllText(readOnly, "x");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            File.WriteAllText(Path.Combine(dir.FullPath, "top.txt"), "y");
            var readOnlyDir = new DirectoryInfo(Path.Combine(dir.FullPath, "a"));
            readOnlyDir.Attributes |= FileAttributes.ReadOnly;

            dir.Dispose();

            Assert.IsFalse(Directory.Exists(dir.FullPath));
        }

        [Test]
        public void Dispose_Twice_IsSafe()
        {
            var dir = new TempDirectory();
            dir.Dispose();
            Assert.DoesNotThrow(dir.Dispose);
        }
    }
}
