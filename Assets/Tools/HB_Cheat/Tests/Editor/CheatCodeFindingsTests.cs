using System.IO;
using System.Text;
using HB.Cheat.Editor;
using NUnit.Framework;

/// <summary>UniqueClassName이 전역 타입 이름 충돌을 보는지 확인하는 용도 (전역 네임스페이스)</summary>
public static class CheatNameTakenProbe { }

namespace HB.Cheat.Tests
{
    /// <summary>2026-10-24 코드 읽기 지적: 치트 파일 클래스 이름, asmdef 판정, 빌드 가드 이름 검사</summary>
    public class CheatCodeFindingsTests
    {
        private string m_Folder;

        [SetUp]
        public void SetUp()
        {
            m_Folder = Path.Combine(Path.GetTempPath(), "HBCheatFindings_" + System.Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(m_Folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Folder)) Directory.Delete(m_Folder, true);
        }

        [TestCase("ItemCheats", true)]
        [TestCase("_Item2", true)]
        [TestCase("아이템Cheats", true)]
        [TestCase("Item Cheats", false)]
        [TestCase("2Item", false)]
        [TestCase("Item-Cheats", false)]
        [TestCase("class", false)]
        [TestCase("Debug", false)]
        [TestCase("Cheat", false)]
        [TestCase("HB", false)]
        [TestCase("List", false)]
        [TestCase("", false)]
        public void ValidateClassName(string name, bool valid)
        {
            Assert.AreEqual(valid, CheatFileTemplates.ValidateClassName(name) == null);
        }

        [Test]
        public void UniqueClassName_ExistingFile_GetsIdentifierNumber()
        {
            File.WriteAllText($"{m_Folder}/ItemCheats.cs", "// existing");
            Assert.AreEqual("ItemCheats2", CheatFileTemplates.UniqueClassName(m_Folder, "ItemCheats"));
            File.WriteAllText($"{m_Folder}/ItemCheats2.cs", "// existing");
            Assert.AreEqual("ItemCheats3", CheatFileTemplates.UniqueClassName(m_Folder, "ItemCheats"));
        }

        [Test]
        public void UniqueClassName_ExistingGlobalType_GetsNumber()
        {
            Assert.AreEqual("CheatNameTakenProbe2", CheatFileTemplates.UniqueClassName(m_Folder, "CheatNameTakenProbe"));
            Assert.AreEqual("FreshCheatsName", CheatFileTemplates.UniqueClassName(m_Folder, "FreshCheatsName"));
        }

        [Test]
        public void AsmdefConstraint_ParsedFromJson()
        {
            Assert.IsTrue(CheatEditorWindow.HasCheatDefineConstraint("{\"name\":\"Game.Cheats\",\"defineConstraints\":[\"HB_CHEAT\",\"DEV_CHEAT\"]}"));
            // 다른 칸에만 HB_CHEAT가 있으면 치트 asmdef가 아니다
            Assert.IsFalse(CheatEditorWindow.HasCheatDefineConstraint(
                "{\"name\":\"Game\",\"defineConstraints\":[],\"versionDefines\":[{\"name\":\"x\",\"expression\":\"1\",\"define\":\"HB_CHEAT\"}]}"));
            Assert.IsFalse(CheatEditorWindow.HasCheatDefineConstraint("{\"name\":\"Game\",\"defineConstraints\":[\"!HB_CHEAT\"]}"));
            Assert.IsFalse(CheatEditorWindow.HasCheatDefineConstraint("not json"));
        }

        [TestCase("\0HB.Cheat\0", true)]
        [TestCase("\0HB.Cheat.UI\0", true)]
        [TestCase("HB.Cheat\0", true)]
        [TestCase("\0HB.CheatSheet\0", false)]
        // 앞쪽은 보지 않는다 (컴파일러가 끝이 같은 문자열을 접어 저장해도 놓치지 않게) → 다른 이름의 꼬리도 걸린다 (안전한 쪽)
        [TestCase("\0MyHB.Cheat\0", true)]
        [TestCase("Game.HB.Cheat\0", true)]
        [TestCase("\0Other\0", false)]
        public void BuildGuard_MatchesWholeNameOnly(string metadata, bool expected)
        {
            Assert.AreEqual(expected, CheatBuildGuard.ContainsMarker(Encoding.ASCII.GetBytes(metadata)));
        }

        [TestCase("HB.Cheat.dll", true)]
        [TestCase("HB.Cheat.Editor.dll", true)]
        [TestCase("HB.Cheat.InGameConsole.dll", true)]
        [TestCase("HB.CheatSheet.dll", false)]
        [TestCase("Game.dll", false)]
        public void BuildGuard_CheatAssemblyFileName(string fileName, bool expected)
        {
            Assert.AreEqual(expected, CheatBuildGuard.IsCheatAssemblyFileName(fileName));
        }

        [Test]
        public void BuildGuard_Utf16StringConstant_NotMatched()
        {
            // 문자열 상수(#US 힙)는 UTF-16이다
            Assert.IsFalse(CheatBuildGuard.ContainsMarker(Encoding.Unicode.GetBytes("\0HB.Cheat\0")));
        }
    }
}
