using System;

namespace BattlePvp.Combat
{
    public sealed class FixedRingBuffer<T>
    {
        private readonly T[] _items;
        private int _head;
        public int Count { get; private set; }
        public int Capacity => _items.Length;

        public FixedRingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[(_head + index) % Capacity];
            }
        }

        public void Add(T value)
        {
            if (Count == Capacity) RemoveFirst();
            _items[(_head + Count) % Capacity] = value;
            Count++;
        }

        public void RemoveFirst()
        {
            if (Count == 0) return;
            _items[_head] = default;
            _head = (_head + 1) % Capacity;
            Count--;
        }

        public void TrimToCount(int maximum)
        {
            maximum = Math.Max(0, maximum);
            while (Count > maximum) RemoveFirst();
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            Count = 0;
        }
    }
}
