using NUnit.Framework;
using UnityEngine;

namespace HB.TestFrameworkHelper.Tests
{
    /// <summary>베이스 픽스처가 테스트마다 Scope를 새로 만들고 끝나면 정리하는지 (두 테스트를 순서대로 돌려 확인)</summary>
    public class HBTestFixtureEditModeTests : HBTestFixture
    {
        private static TestScope s_FirstScope;
        private static GameObject s_FirstObject;

        [SetUp]
        public void DerivedSetUp()
        {
            Assert.IsNotNull(Scope, "베이스 SetUp이 파생 SetUp보다 먼저 돌아야 함");
        }

        [Test, Order(1)]
        public void A_ScopeIsReadyInTest()
        {
            Assert.IsFalse(Scope.IsDisposed);
            s_FirstScope = Scope;
            s_FirstObject = Scope.NewGameObject("HBTestFixture_EditMode");
        }

        [Test, Order(2)]
        public void B_PreviousTestScopeWasDisposed()
        {
            Assume.That(s_FirstScope, Is.Not.Null, "A_ScopeIsReadyInTest와 같이 돌려야 확인할 수 있음");
            Assert.IsTrue(s_FirstScope.IsDisposed);
            Assert.IsTrue(s_FirstObject == null);
            Assert.AreNotSame(s_FirstScope, Scope);
        }
    }
}
