using System;

namespace HB.PerfProbe
{
    /// <summary>
    /// 고정 크기 링 버퍼. 꽉 차면 가장 오래된 것을 덮는다. 매 프레임 넣으므로 Add는 할당하지 않는다.
    /// 스레드 안전하지 않다 — 여러 스레드에서 쓰면 호출하는 쪽이 잠근다.
    /// </summary>
    internal sealed class Ring<T>
    {
        readonly T[] _items;
        int _start;
        int _count;

        public Ring(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public void Add(in T item)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = item;
                _count++;
            }
            else
            {
                _items[_start] = item;
                _start = (_start + 1) % _items.Length;
            }
        }

        /// <summary>i = 0이 가장 오래된 것.</summary>
        public T this[int i]
        {
            get
            {
                if ((uint)i >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(i));
                return _items[(_start + i) % _items.Length];
            }
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _start = 0;
            _count = 0;
        }
    }
}
