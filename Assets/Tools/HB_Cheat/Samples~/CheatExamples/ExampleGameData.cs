#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

// 치트 예제용 가짜 게임 코드 (테이블, 플레이어, 네트워크).
// 실제 프로젝트에서는 이미 있는 게임 코드이므로 DEV_CHEAT로 감싸지 않는다.
namespace CheatExamples
{
    public enum ItemGrade
    {
        Common = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }

    public enum StatType
    {
        Attack,
        Defense,
        MoveSpeed,
        CriticalRate
    }

    public enum CurrencyType
    {
        Gold = 1,
        Gem = 2,
        Stamina = 3
    }

    public sealed class ItemData
    {
        public int Id;
        public string Name;
        public ItemGrade Grade;
        public int MaxEnchant;
    }

#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class ItemTable
    {
        private static readonly List<ItemData> s_Items = new List<ItemData>
        {
            new ItemData { Id = 1001, Name = "낡은 검", Grade = ItemGrade.Common, MaxEnchant = 5 },
            new ItemData { Id = 1002, Name = "기사의 검", Grade = ItemGrade.Rare, MaxEnchant = 10 },
            new ItemData { Id = 1003, Name = "용살자", Grade = ItemGrade.Legendary, MaxEnchant = 20 },
            new ItemData { Id = 2001, Name = "가죽 갑옷", Grade = ItemGrade.Common, MaxEnchant = 5 },
            new ItemData { Id = 2002, Name = "미스릴 갑옷", Grade = ItemGrade.Epic, MaxEnchant = 15 },
            new ItemData { Id = 3001, Name = "체력 물약", Grade = ItemGrade.Common, MaxEnchant = 0 },
        };

        /// <summary>테이블을 다시 읽을 때마다 올라간다 (치트 목록 캐시 무효화용)</summary>
        public static int Version { get; private set; } = 1;

        public static IReadOnlyList<ItemData> All => s_Items;

        public static ItemData Get(int id) => s_Items.Find(x => x.Id == id);

        public static void Reload() => Version++;
    }

    /// <summary>클라에서 직접 바꾸는 값 (Local 치트 예제용)</summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class ExamplePlayer
    {
        public static int Level = 1;
        public static bool GodMode;
        public static int Hp = 100;
        public static int MaxHp = 100;
        public static string Nickname = "Player";
        private static readonly Dictionary<StatType, float> s_Stats = new Dictionary<StatType, float>();

        public static float GetStat(StatType type) => s_Stats.TryGetValue(type, out float value) ? value : 0f;
        public static void SetStat(StatType type, float value) => s_Stats[type] = value;
    }

    // ---- 네트워크 (가짜) ----

    public struct C2P_RequestCheat
    {
        public string Command;
    }

    public enum CheatResultCode
    {
        Success,
        InvalidCommand,
        NoPermission
    }

    public struct P2C_ResultCheat
    {
        public CheatResultCode Result;
    }

    /// <summary>패킷을 보내면 잠시 뒤 서버 응답 이벤트가 온다고 가정한 가짜 네트워크</summary>
#if UNITY_AUTOSTATICS_CLEANUP_ANALYSIS_ENABLED || UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class ExampleNetwork
    {
        public static bool IsConnected = true;
        public static event Action<P2C_ResultCheat> OnResultCheat;

        public static async void Send(C2P_RequestCheat packet)
        {
            Debug.Log($"[ExampleNetwork] C2P_RequestCheat \"{packet.Command}\"");
            await Awaitable.WaitForSecondsAsync(0.3f);

            // 서버는 명령 이름만 보고 성공/실패만 돌려준다
            bool known = packet.Command.StartsWith("item_") || packet.Command.StartsWith("currency_") || packet.Command.StartsWith("mail_");
            OnResultCheat?.Invoke(new P2C_ResultCheat { Result = known ? CheatResultCode.Success : CheatResultCode.InvalidCommand });
        }
    }
}
