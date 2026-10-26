using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper.Tests
{
    public class TestScopePlayModeTests
    {
        [UnityTest]
        public IEnumerator Dispose_SchedulesDestroy_CompletesNextFrame()
        {
            DestroyProbe.ResetCounts();
            var scope = new TestScope();
            GameObject go = scope.NewGameObject("HBTestScope_PlayMode", typeof(DestroyProbe));

            scope.Dispose();

            // OnDisable은 Unity가 Destroy 요청 때 바로 부른다 (6000.3 실측) — TestScope가 따로 비활성화하지는 않는다
            Assert.IsTrue(go != null, "PlayMode의 Dispose는 파괴 예약까지 — 같은 프레임에는 아직 있음");
            Assert.AreEqual(0, DestroyProbe.DestroyedCount);

            yield return null;

            Assert.IsTrue(go == null);
            Assert.AreEqual(1, DestroyProbe.DestroyedCount);
        }

        [UnityTest]
        public IEnumerator Track_Component_DestroysOnlyThatComponent()
        {
            var owner = new TestScope();
            try
            {
                GameObject go = owner.NewGameObject("HBTestScope_PlayModeOwner", typeof(BoxCollider), typeof(DestroyProbe));
                var inner = new TestScope();
                inner.Track(go.GetComponent<BoxCollider>());
                inner.Dispose();

                yield return null;

                Assert.IsTrue(go != null);
                Assert.IsTrue(go.activeSelf);
                Assert.IsTrue(go.GetComponent<BoxCollider>() == null);
                Assert.IsTrue(go.GetComponent<DestroyProbe>() != null);
            }
            finally
            {
                owner.Dispose();
            }
        }
    }

    /// <summary>베이스 픽스처가 PlayMode에서 한 프레임 기다려, 다음 테스트가 시작될 때 이전 객체가 파괴돼 있는지</summary>
    public class HBTestFixturePlayModeTests : HBTestFixture
    {
        private static GameObject s_FromUnityTest;
        private static GameObject s_FromPlainTest;
        private static bool s_UnityTestRan;
        private static bool s_PlainTestRan;

        [UnityTest, Order(1)]
        public IEnumerator A_UnityTest_CreatesObject()
        {
            DestroyProbe.ResetCounts();
            s_FromUnityTest = Scope.NewGameObject("HBTestFixture_PlayMode_A", typeof(DestroyProbe));
            s_UnityTestRan = true;
            yield return null;
        }

        [Test, Order(2)]
        public void B_ObjectFromUnityTestIsDestroyed()
        {
            Assume.That(s_UnityTestRan, "A와 같이 돌려야 확인할 수 있음");
            Assert.IsTrue(s_FromUnityTest == null);
            Assert.AreEqual(1, DestroyProbe.DestroyedCount);
        }

        [Test, Order(3)]
        public void C_PlainTest_CreatesObject()
        {
            DestroyProbe.ResetCounts();
            s_FromPlainTest = Scope.NewGameObject("HBTestFixture_PlayMode_C", typeof(DestroyProbe));
            s_PlainTestRan = true;
        }

        [Test, Order(4)]
        public void D_ObjectFromPlainTestIsDestroyed()
        {
            Assume.That(s_PlainTestRan, "C와 같이 돌려야 확인할 수 있음");
            Assert.IsTrue(s_FromPlainTest == null, "[Test]에서도 정리 뒤 한 프레임을 기다려야 함");
            Assert.AreEqual(1, DestroyProbe.DestroyedCount);
        }
    }
}
