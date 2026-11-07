using System;
using System.Collections.Generic;

namespace HB.TestFrameworkHelper.Samples
{
    /// <summary>
    /// 칸 수가 정해진 인벤토리입니다. 배열을 미리 잡아 두어 <see cref="Add"/> · <see cref="Clear"/>는 GC 할당을 하지 않습니다.
    /// <see cref="Describe"/>는 화면 · 로그용이라 할당합니다.
    /// </summary>
    public sealed class ExampleInventory
    {
        private readonly int[] m_ItemIds; // 0 = 빈 칸
        private readonly int[] m_Counts;
        private readonly int m_MaxStack;

        public ExampleInventory(int capacity, int maxStack)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxStack <= 0) throw new ArgumentOutOfRangeException(nameof(maxStack));

            m_ItemIds = new int[capacity];
            m_Counts = new int[capacity];
            m_MaxStack = maxStack;
        }

        /// <summary>같은 아이템 칸에 먼저 쌓고, 남으면 빈 칸에 넣습니다. 넣지 못한 개수를 돌려줍니다.</summary>
        public int Add(int itemId, int count)
        {
            if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            int remaining = count;
            for (int i = 0; i < m_ItemIds.Length && remaining > 0; i++)
            {
                if (m_ItemIds[i] != itemId) continue;
                int put = Math.Min(m_MaxStack - m_Counts[i], remaining);
                m_Counts[i] += put;
                remaining -= put;
            }
            for (int i = 0; i < m_ItemIds.Length && remaining > 0; i++)
            {
                if (m_ItemIds[i] != 0) continue;
                int put = Math.Min(m_MaxStack, remaining);
                m_ItemIds[i] = itemId;
                m_Counts[i] = put;
                remaining -= put;
            }
            return remaining;
        }

        public int CountOf(int itemId)
        {
            int total = 0;
            for (int i = 0; i < m_ItemIds.Length; i++)
            {
                if (m_ItemIds[i] == itemId) total += m_Counts[i];
            }
            return total;
        }

        public void Clear()
        {
            Array.Clear(m_ItemIds, 0, m_ItemIds.Length);
            Array.Clear(m_Counts, 0, m_Counts.Length);
        }

        /// <summary>칸마다 한 줄: <c>slot 0: item 1001 x5</c>, 빈 칸은 <c>slot 1: empty</c>.</summary>
        public List<string> Describe()
        {
            var lines = new List<string>(m_ItemIds.Length);
            for (int i = 0; i < m_ItemIds.Length; i++)
            {
                lines.Add(m_ItemIds[i] == 0 ? $"slot {i}: empty" : $"slot {i}: item {m_ItemIds[i]} x{m_Counts[i]}");
            }
            return lines;
        }
    }
}
