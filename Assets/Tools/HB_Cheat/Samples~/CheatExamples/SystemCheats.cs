#if DEV_CHEAT
using HB.Cheat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheatExamples
{
    /// <summary>
    /// 클라 시스템 치트 예제. 탭: System > (Time Scale, Load Scene, Target FPS, Reload Tables)
    /// Order로 탭 안 순서를 정한다 (작을수록 앞).
    /// </summary>
    public static class SystemCheats
    {
        // float + 소수 step
        [Cheat("system.timescale", "System/Time Scale", Order = 0)]
        public static string SetTimeScale([CheatArg("배속"), CheatRange(0, 10, Step = 0.25)] float scale = 1f)
        {
            Time.timeScale = scale;
            return $"Time.timeScale = {scale}";
        }

        // string 인자를 목록에서 고르기 (Provider가 string 값을 준다)
        [Cheat("system.loadscene", "System/Load Scene", Order = 1)]
        public static CheatResult LoadScene([CheatArg("씬"), CheatOptions(typeof(BuildSceneProvider))] string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                return CheatResult.Fail($"'{sceneName}' 씬을 불러올 수 없음");

            SceneManager.LoadScene(sceneName);
            return CheatResult.Ok();
        }

        [Cheat("system.fps", "System/Target FPS", Order = 2)]
        public static void SetTargetFps([CheatArg("FPS"), CheatRange(15, 240, Step = 15)] int fps = 60)
        {
            Application.targetFrameRate = fps;
        }

        // 테이블 다시 읽기 → Provider.Version이 바뀌어 목록이 다시 만들어진다
        [Cheat("system.reloadtables", "System/Reload Tables", Order = 10, Description = "테이블 리로드 (치트 목록 캐시도 갱신)")]
        public static string ReloadTables()
        {
            ItemTable.Reload();
            return $"ItemTable v{ItemTable.Version}";
        }
    }
}
#endif
