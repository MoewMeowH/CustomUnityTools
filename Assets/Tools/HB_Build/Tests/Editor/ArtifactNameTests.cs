using HB.CustomBuild.Editor;
using NUnit.Framework;

namespace HB.CustomBuild.Tests
{
    /// <summary> 산출물 이름: {제품명}_v{버전}[_{버전코드}]_{빌드타입}[_DEVBUILD][_{소스}][_Jenkins{번호}] </summary>
    public class ArtifactNameTests
    {
        [Test]
        public void Jenkins_LiveDev_Dirty_AllParts()
        {
            Assert.AreEqual("MyGame_v1.2.0_45_LIVE_DEVBUILD_a1b2c3d-dirty_Jenkins128",
                BuildHelper.ComposeArtifactName("MyGame", "1.2.0", "45", EBuildType.LIVE, true, "a1b2c3d-dirty", "128"));
        }

        [Test]
        public void Local_EmptyVersionCode_OmitsPart()
        {
            Assert.AreEqual("MyGame_v1.2.0_DEV_a1b2c3d",
                BuildHelper.ComposeArtifactName("MyGame", "1.2.0", "", EBuildType.DEV, false, "a1b2c3d", null));
        }

        [Test]
        public void Jenkins_NoGit_UnknownRevisionMarks()
        {
            Assert.AreEqual("MyGame_v1.0_7_QA_nogit_Jenkins12",
                BuildHelper.ComposeArtifactName("MyGame", "1.0", "7", EBuildType.QA, false, "nogit", "12"));
            Assert.AreEqual("MyGame_v1.0_7_QA_a1b2c3d-unknown",
                BuildHelper.ComposeArtifactName("MyGame", "1.0", "7", EBuildType.QA, false, "a1b2c3d-unknown", null));
        }

        [Test]
        public void NoRevision_OmitsPart()
        {
            // 빌드 시작 전(창 미리보기 등)에는 소스 칸이 아직 비어 있다
            Assert.AreEqual("MyGame_v1.0_DEV",
                BuildHelper.ComposeArtifactName("MyGame", "1.0", null, EBuildType.DEV, false, null, null));
        }
    }
}
