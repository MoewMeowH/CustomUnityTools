using HB.Cheat.Editor;
using NUnit.Framework;
using UnityEditor;

namespace HB.Cheat.Tests
{
    /// <summary>CLI 명령(파이프라인 패키지가 있을 때만 컴파일): 에디트 모드에서는 모두 not_initialized JSON으로 거절</summary>
    public class CheatCliCommandsTests
    {
        [SetUp]
        public void RequireEditMode()
        {
            Assume.That(EditorApplication.isPlaying, Is.False);
        }

        [Test]
        public void List_InEditMode_IsRejected()
        {
            string json = CheatCliCommands.List();
            StringAssert.Contains("\"ok\":false", json);
            StringAssert.Contains(CheatErrorCode.NotInitialized, json);
        }

        [Test]
        public void Run_InEditMode_IsRejected()
        {
            string json = CheatCliCommands.Run("hb.test.none", "");
            StringAssert.Contains("\"ok\":false", json);
            StringAssert.Contains(CheatErrorCode.NotInitialized, json);
        }

        [Test]
        public void History_InEditMode_IsRejected()
        {
            StringAssert.Contains(CheatErrorCode.NotInitialized, CheatCliCommands.History());
        }
    }
}
