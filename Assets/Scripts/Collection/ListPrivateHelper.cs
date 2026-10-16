using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

public static class ListPrivateHelper
{

    public static void AccessInternalArraySort<T>(List<T> list, IComparer<T> comparer)
    {
        var helper = UnsafeUtility.As<List<T>, ListPrivateFieldAccess<T>>(ref list);
        Array.Sort(helper._items, 0, helper._size, comparer);
    }

    public static Span<T> AccessInternalSpan<T>(List<T> list)
    {
        var helper = UnsafeUtility.As<List<T>, ListPrivateFieldAccess<T>>(ref list);
        var span = new Span<T>(helper._items, 0, helper._size);
        return span;
    }

    private class ListPrivateFieldAccess<T>
    {
#pragma warning disable CS0649
#pragma warning disable CS8618
        public T[] _items; // Do not rename (binary serialization)
#pragma warning restore CS8618
        public int _size; // Do not rename (binary serialization)
        public int _version; // Do not rename (binary serialization)
#pragma warning restore CS0649
    }
}