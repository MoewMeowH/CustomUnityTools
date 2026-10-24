#if DEV_CHEAT
using System.Collections.Generic;
using HB.Cheat;
using UnityEngine.SceneManagement;

namespace CheatExamples
{
    /// <summary>아이템 테이블 전체. 치트를 고를 때 · 실행할 때 다시 읽으므로 테이블을 다시 로드해도 따로 할 일이 없다.</summary>
    public sealed class ItemProvider : ICheatOptionProvider
    {
        public void Collect(CheatOptionContext context, List<CheatOption> results)
        {
            foreach (var item in ItemTable.All)
                results.Add(new CheatOption(item.Id, $"[{item.Grade}] {item.Name}"));
        }
    }

    /// <summary>강화 가능한 아이템만 (MaxEnchant > 0)</summary>
    public sealed class EnchantableItemProvider : ICheatOptionProvider
    {
        public void Collect(CheatOptionContext context, List<CheatOption> results)
        {
            foreach (var item in ItemTable.All)
            {
                if (item.MaxEnchant > 0)
                    results.Add(new CheatOption(item.Id, $"{item.Name} (max +{item.MaxEnchant})"));
            }
        }
    }

    /// <summary>
    /// 앞 인자(itemId)에 따라 강화 단계가 달라진다. [CheatOptions(..., DependsOn = new[] { "itemId" })]와 함께 쓴다.
    /// itemId가 바뀌면 이 목록은 다시 수집되고, 이미 고른 값은 초기화된다.
    /// </summary>
    public sealed class EnchantLevelProvider : ICheatOptionProvider
    {
        public void Collect(CheatOptionContext context, List<CheatOption> results)
        {
            if (!context.TryGet("itemId", out int itemId)) return;   // 아이템을 아직 안 골랐으면 빈 목록

            var item = ItemTable.Get(itemId);
            if (item == null) return;

            for (int level = 1; level <= item.MaxEnchant; level++)
                results.Add(new CheatOption(level, $"+{level}"));
        }
    }

    /// <summary>문자열 값도 목록으로 고를 수 있다 (Build Settings에 등록된 씬)</summary>
    public sealed class BuildSceneProvider : ICheatOptionProvider
    {
        public void Collect(CheatOptionContext context, List<CheatOption> results)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                results.Add(new CheatOption(name, $"{i}: {name}"));
            }
        }
    }
}
#endif
