using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.TestTools;

namespace HB.TestFrameworkHelper
{
    /// <summary>
    /// 테스트마다 <see cref="Scope"/>를 만들고 끝나면 정리하는 얇은 베이스입니다.
    /// <para>PlayMode는 정리한 뒤 한 프레임 기다려 Destroy가 끝난 다음 테스트로 넘어갑니다. 정리 중 예외도 그 뒤에 보고합니다.</para>
    /// <para>Test Framework는 [UnitySetUp] → [SetUp] → 테스트 → [TearDown] → [UnityTearDown] 순으로 부르고, 베이스는 파생보다 먼저 시작해 나중에 끝납니다.
    /// Scope를 베이스 [UnitySetUp]에서 만들고 베이스 [UnityTearDown]에서 정리하므로 파생 클래스의 어느 SetUp · TearDown에서도 Scope를 쓸 수 있습니다.
    /// 파생 클래스에서 HbScope로 시작하는 이름의 메서드를 다시 선언하지 마세요 (숨기면 정리가 빠집니다).</para>
    /// </summary>
    public abstract class HBTestFixture
    {
        private Exception m_CleanupError;

        protected TestScope Scope { get; private set; }

        [UnitySetUp]
        public IEnumerator HbScopeUnitySetUp()
        {
            Scope = new TestScope();
            m_CleanupError = null;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator HbScopeUnityTearDown()
        {
            try
            {
                Scope?.Dispose();
            }
            catch (Exception e)
            {
                m_CleanupError = e;
            }

            // Destroy(파괴 예약)가 끝나도록 한 프레임 넘긴다 — 정리가 실패했어도 기다린 뒤 보고
            if (Application.isPlaying) yield return null;

            Exception error = m_CleanupError;
            m_CleanupError = null;
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
