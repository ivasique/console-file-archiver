using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GzipArchiver
{
    // Кольцевой буфер с объектами-локерами для его ячеек.
    class CircularBuffer
    {
        protected byte[][] Storage;
        private object[] _blocksLockers;
        private int _writePosition;
        private int _readPosition;
        private int _count;
        private object _countLocker;

        internal CircularBuffer(int capacity)
        {
            Storage = new byte[capacity][];
            _blocksLockers = new object[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _blocksLockers[i] = new object();
            }
            _writePosition = 0;
            _readPosition = 0;
            _count = 0;
            _countLocker = new object();
        }

        internal int Capacity
        {
            get { return Storage.Length; }
        }
        public int WritePosition
        {
            get { return _writePosition; }
            protected set
            {
                if (value < 0 || value > Capacity)
                {
                    throw new ArgumentOutOfRangeException();
                }
                _writePosition = value;
            }
        }
        internal int ReadPosition
        {
            get { return _readPosition; }
        }
        public int Count
        {
            get { return _count; }
            protected set
            {
                if (value < 0 || value > Capacity)
                {
                    throw new ArgumentOutOfRangeException();
                }
                lock (_countLocker)
                {
                    _count = value;
                }
            }
        }
        protected object[] BlockLockers
        {
            get { return _blocksLockers; }
        }

        internal void AcquireBlockLock(int index)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException();
            }
            Monitor.Enter(_blocksLockers[index]);
        }
        internal void ReleaseBlockLock(int index)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException();
            }
                Monitor.Exit(_blocksLockers[index]);
        }
        internal int WriteBlock(ref byte[] block)
        {
            if (_count < Capacity)
            {
                if (Monitor.IsEntered(_blocksLockers[_writePosition]))
                {
                    Storage[_writePosition] = block;
                    block = null;
                    lock ( _countLocker )
                    {
                        _count++;
                    }
                    _writePosition = ++_writePosition % Capacity;
                    return 0;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return 1;
            }
        }
        internal byte[] ReadBlock()
        {
            if (_count > 0 && Monitor.IsEntered(_blocksLockers[_readPosition]))
            {
                byte[] block = Storage[_readPosition];
                Storage[_readPosition] = null;
                lock ( _countLocker )
                {
                    _count--;
                }
                _readPosition = ++_readPosition % Capacity;
                return block;
            }
            else
            {
                return null;
            }
        }
        internal void DecreaseCount()
        {
            if (Count < 0)
            {
                throw new OverflowException();
            }
            lock (_countLocker)
            {
                _count--;
            }
        }
        internal void IncreaseCount()
        {
            if (Count == Capacity)
            {
                throw new OverflowException();
            }
            lock (_countLocker)
            {
                _count++;
            }
        }
    }
}
