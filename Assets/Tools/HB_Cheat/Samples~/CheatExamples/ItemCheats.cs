#if DEV_CHEAT
using HB.Cheat;

namespace CheatExamples
{
    /// <summary>
    /// 서버 치트 예제. 반환한 문자열이 C2P_RequestCheat로 서버에 간다.
    /// 탭: Item > (Add, Enchant, Grade Up, Clear Inventory)
    /// </summary>
    public static class ItemCheats
    {
        // 목록 선택(테이블) + 숫자 범위
        [Cheat("item.add", "Item/Add", Target = CheatTarget.Server, Description = "아이템 지급", Order = 0)]
        public static string Add(
            [CheatArg("아이템"), CheatOptions(typeof(ItemProvider))] int itemId,
            [CheatArg("개수"), CheatRange(1, 9999, Step = 10)] int count = 1)
        {
            return CheatCommand.Join("item_add", itemId, count);   // "item_add 1001 10"
        }

        // 앞 인자에 따라 바뀌는 목록 (DependsOn)
        [Cheat("item.enchant", "Item/Enchant", Target = CheatTarget.Server, Description = "강화 단계 설정 (아이템마다 최대 단계가 다름)", Order = 1)]
        public static string Enchant(
            [CheatArg("아이템"), CheatOptions(typeof(EnchantableItemProvider))] int itemId,
            [CheatArg("강화"), CheatOptions(typeof(EnchantLevelProvider), DependsOn = new[] { "itemId" })] int level)
        {
            return CheatCommand.Join("item_enchant", itemId, level);
        }

        // enum은 자동으로 목록이 된다. 서버로는 정수 값이 간다 (Epic → 3)
        [Cheat("item.grade", "Item/Grade Up", Target = CheatTarget.Server, Order = 2)]
        public static string SetGrade(
            [CheatArg("아이템"), CheatOptions(typeof(ItemProvider))] int itemId,
            [CheatArg("등급")] ItemGrade grade = ItemGrade.Epic)
        {
            return CheatCommand.Join("item_grade", itemId, grade);
        }

        // 인자 없는 치트
        [Cheat("item.clear", "Item/Clear Inventory", Target = CheatTarget.Server, Description = "인벤토리 비우기", Order = 10)]
        public static string ClearInventory() => "item_clear";
    }
}
#endif
