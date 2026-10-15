#if DEV_CHEAT
using HB.Cheat;
using UnityEngine;

namespace CheatExamples
{
    /// <summary>
    /// 클라 치트 예제 (Local). 반환형별 동작:
    ///   void        → 예외가 없으면 성공
    ///   bool        → false면 실패
    ///   string      → 성공 + 메시지
    ///   CheatResult → 성공/실패와 메시지를 직접 지정
    /// 탭: Actor > (Level, God Mode, Heal, Nickname) / Actor > Stat > (Set, Reset)
    /// </summary>
    public static class ActorCheats
    {
        // void + 숫자 범위 (패널 키패드로 입력)
        [Cheat("actor.level", "Actor/Level", Description = "레벨 설정")]
        public static void SetLevel([CheatArg("레벨"), CheatRange(1, 100)] int level = 1)
        {
            ExamplePlayer.Level = level;
            Debug.Log($"[Cheat] Level = {level}");
        }

        // bool 인자 → 토글, string 반환 → 결과 메시지
        [Cheat("actor.god", "Actor/God Mode")]
        public static string SetGodMode([CheatArg("무적")] bool enabled = true)
        {
            ExamplePlayer.GodMode = enabled;
            return enabled ? "무적 ON" : "무적 OFF";
        }

        // bool 반환 → false면 실패로 표시
        [Cheat("actor.heal", "Actor/Heal", Description = "체력 회복 (이미 가득이면 실패)")]
        public static bool Heal()
        {
            if (ExamplePlayer.Hp >= ExamplePlayer.MaxHp) return false;
            ExamplePlayer.Hp = ExamplePlayer.MaxHp;
            return true;
        }

        // string 인자 → 텍스트 입력
        [Cheat("actor.nickname", "Actor/Nickname")]
        public static void SetNickname([CheatArg("닉네임")] string nickname = "Tester")
        {
            ExamplePlayer.Nickname = nickname;
        }

        // 3단 경로 (Actor > Stat > Set). enum + float(소수 step)
        [Cheat("actor.stat.set", "Actor/Stat/Set")]
        public static CheatResult SetStat(
            [CheatArg("스탯")] StatType stat,
            [CheatArg("값"), CheatRange(0, 1000, Step = 0.5)] float value = 10f)
        {
            if (stat == StatType.CriticalRate && value > 100f)
                return CheatResult.Fail("치명타 확률은 100 이하");

            ExamplePlayer.SetStat(stat, value);
            return CheatResult.Ok($"{stat} = {value}");
        }

        [Cheat("actor.stat.reset", "Actor/Stat/Reset")]
        public static string ResetStats()
        {
            foreach (StatType stat in System.Enum.GetValues(typeof(StatType)))
                ExamplePlayer.SetStat(stat, 0f);
            return "모든 스탯 0";
        }
    }
}
#endif
