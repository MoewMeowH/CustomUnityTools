using System.Collections.Generic;
using System.Linq;

public class IndexedMap<TKey, TValue>
{
    private readonly List<(TKey Key, TValue Value)> m_List = new();
    private readonly Dictionary<TKey, int> m_KeyToIndex = new();

    public IReadOnlyList<(TKey Key, TValue Value)> AsList() => m_List;
    public int Count => m_List.Count;
    public bool IsEmpty() => m_List.Count == 0;

    public void Clear()
    {
        m_KeyToIndex.Clear();
        m_List.Clear();
    }

    public bool Add(TKey key, TValue value)
    {
        if (m_KeyToIndex.ContainsKey(key)) return false;
        m_KeyToIndex.Add(key, m_List.Count);
        m_List.Add((key, value));
        return true;
    }

    public TValue Get(TKey key)
    {
        if (m_KeyToIndex.TryGetValue(key, out int index))
            return m_List[index].Value;

        return default;
    }

    public bool TryGet(TKey key, out TValue value)
    {
        value = default;
        var ret = false;
        if (m_KeyToIndex.TryGetValue(key, out int index))
        {
            value = m_List[index].Value;
            ret = true;
        }

        return ret;
    }

    public bool Remove(TKey key)
    {
        if (!m_KeyToIndex.Remove(key, out int index)) return false;
        m_List.RemoveAt(index);
        for (int i = index; i < m_List.Count; i++)
        {
            m_KeyToIndex[m_List[i].Key] = i;
        }
        return true;
    }
}

public class IndexedSet<TValue>
{
    private readonly List<TValue> m_List = new();
    private readonly HashSet<TValue> m_Set = new();

    public IReadOnlyList<TValue> AsList() => m_List;

    public int Count => m_List.Count;

    public int FindIndex(TValue value) => m_List.FindIndex(x => x.Equals(value));

    public bool IsEmpty() => m_List is { Count: 0 };

    public void Clear()
    {
        m_Set.Clear();
        m_List.Clear();
    }

    public bool Add(TValue value)
    {
        var res = m_Set.Add(value);
        if (res)
            m_List.Add(value);

        return res;
    }

    public bool Remove(TValue value)
    {
        var res = m_Set.Remove(value);
        if (res)
            m_List.Remove(value);

        return res;
    }
}