using System;
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace HB.Cheat.Editor
{
    /// <summary>
    /// HB_CHEAT가 켜져 있을 때만 컴파일되는 에디터 어셈블리(Generator)가 채운다.
    /// 이 어셈블리는 심볼과 무관하게 컴파일되어야 하므로 런타임 타입을 직접 참조하지 않는다.
    /// (어셈블리 로드 때 채우는 훅이라 Play 진입 때 지우지 않는다 — 도메인 리로드에서만 다시 채워짐)
    /// </summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class CheatEditorHooks
    {
        /// <summary>레지스트리 생성. 인자: silent. 반환: 성공 여부</summary>
        public static Func<bool, bool> Generate;

        /// <summary>Cheats 탭 그리기</summary>
        public static Action DrawCheatsTab;

        public static bool IsGeneratorAvailable => Generate != null;
    }
}
