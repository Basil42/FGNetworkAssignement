using System.Collections.Generic;

namespace Script.Utilities
{
    public struct CircularBuffer<T>
    {
        private T[] _buffer;
        public readonly int Size;

        public CircularBuffer(int size)
        {
            this.Size = size;
            _buffer = new T[this.Size];
        }
        public T this[uint index]
        {
            get => _buffer[index];
            set => _buffer[index] = value;
        }
        public void Clear() => _buffer = new T[Size];

        public T this[int index]
        {
            get => _buffer[index];
            set => _buffer[index] = value;
        }
    }
}