using System;
using UnityEditor.SceneManagement;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>에디터 임시 자원을 만들어 바로 <see cref="TestScope"/>에 맡깁니다 (같은 역순 스택에서 정리).</summary>
    public static class EditorTestScopeExtensions
    {
        public static TempDirectory NewTempDirectory(this TestScope scope)
        {
            ThrowIfUnusable(scope);
            return scope.Own(new TempDirectory());
        }

        public static TempAssetFolder NewTempAssetFolder(this TestScope scope)
        {
            ThrowIfUnusable(scope);
            return scope.Own(new TempAssetFolder());
        }

        public static TempScene NewTempScene(this TestScope scope, NewSceneSetup setup = NewSceneSetup.EmptyScene)
        {
            ThrowIfUnusable(scope);
            return scope.Own(new TempScene(setup));
        }

        // 자원을 만들기 전에 확인한다 (Dispose된 Scope에 씬을 바꾸거나 폴더를 만들지 않도록)
        private static void ThrowIfUnusable(TestScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(TestScope));
        }
    }
}
