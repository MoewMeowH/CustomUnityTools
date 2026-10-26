using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>파생 클래스의 [UnitySetUp] · [SetUp] · [TearDown] · [UnityTearDown] 어디서든 Scope를 쓸 수 있는지</summary>
    public class HBTestFixtureUnityHooksTests : HBTestFixture
    {
        private GameObject m_FromUnitySetUp;

        [UnitySetUp]
        public IEnumerator DerivedUnitySetUp()
        {
            Assert.IsNotNull(Scope, "베이스 [UnitySetUp]이 먼저 돌아야 함");
            Assert.IsFalse(Scope.IsDisposed);
            m_FromUnitySetUp = Scope.NewGameObject("HBTestFixture_UnitySetUp");
            yield break;
        }

        [TearDown]
        public void DerivedTearDown()
        {
            Assert.IsFalse(Scope.IsDisposed, "파생 [TearDown] 때는 아직 정리 전이어야 함");
        }

        [UnityTearDown]
        public IEnumerator DerivedUnityTearDown()
        {
            Assert.IsFalse(Scope.IsDisposed, "파생 [UnityTearDown] 때는 아직 정리 전이어야 함");
            yield break;
        }

        [Test]
        public void ObjectFromUnitySetUp_IsUsableInTest()
        {
            Assert.IsTrue(m_FromUnitySetUp != null);
        }

        [UnityTest]
        public IEnumerator ObjectFromUnitySetUp_IsUsableInUnityTest()
        {
            yield return null;
            Assert.IsTrue(m_FromUnitySetUp != null);
        }
    }
}
