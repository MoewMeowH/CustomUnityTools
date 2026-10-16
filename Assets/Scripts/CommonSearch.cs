using System.Collections.Generic;
using System;

public class CommonSearch<Data> where Data : class
{
    protected List<Data> m_ListSource = null;
    protected List<Data> m_ListResult = null;

    public List<Data> ListResult { get { return m_ListResult; } }

    public CommonSearch(List<Data> listSource)
    {
        m_ListSource = new List<Data>(listSource);
        m_ListResult = new List<Data>(listSource);
    }

    public void Search(Predicate<Data> predicate)
    {
        if (m_ListSource == null)
            return;

        m_ListResult.Clear();
        m_ListResult.AddRange(m_ListSource.FindAll(predicate));
    }

    public Data Find(Predicate<Data> predicate)
    {
        if (m_ListSource == null)
            return null;

        return m_ListSource.Find(predicate);
    }
}