using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GzipArchiver
{
    // Кольцевой буфер, хранилище которого уловно разделено на две части (порции).
    class PortionedCircularBuffer : CircularBuffer
    {
        private int _numberOfBlocksPerPortion;

        internal PortionedCircularBuffer(int numberOfBlocksPerPortion) : base(numberOfBlocksPerPortion * 2)
        {
            _numberOfBlocksPerPortion = numberOfBlocksPerPortion;
        }

        internal int PortionSize
        {
            get { return _numberOfBlocksPerPortion; }
        }

        internal byte[] ReadBlock(int index)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (Monitor.IsEntered(BlockLockers[index]))
            {
                DecreaseCount();
                return Storage[index];
            }
            else
            {
                return null;
            }
        }
        internal int WriteBlock(ref byte[] block, int index)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (Monitor.IsEntered(BlockLockers[index]))
            {
                Storage[index] = block;
                IncreaseCount();
                block = null;
                return 0;
            }
            else
            {
                return 1;
            }
        }
        internal byte[] Peek()
        {
            if (Count > 0)
            {
                return Storage[ReadPosition];
            }
            else
            {
                return null;
            }
        }
        internal byte[] Peek(int index)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (Monitor.IsEntered(BlockLockers[index]))
            {
                return Storage[index];
            }
            else
            {
                return null;
            }
        }
        // Производит слияние нескольких блоков. Результат заносится в первый по счёту блок.
        // Далее происходит сдвиг на соответсвующее число позиций всех остальных блоков, чтобы закрыть "дырку".
        internal void MergeBlocks(int startPosition, int numberOfBlocks)
        {
            // На момент операции буфер считается целиком заполенным. Проверка этого усливия на совести пользователя метода.
            Count = Capacity;
            // Слияние блоков
            if (numberOfBlocks < 1 || (startPosition < 0 && startPosition >= Capacity))
            {
                throw new ArgumentOutOfRangeException();
            }
            int currentCount = Storage[startPosition].Length;
            int summaryBlockSize = 0;
            int followingBlockPosition = (startPosition + numberOfBlocks) % Capacity;
            for (int i = startPosition; i != followingBlockPosition; i = ++i % Capacity)
            {
                summaryBlockSize += Storage[i].Length;
            }
            Array.Resize(ref Storage[startPosition], summaryBlockSize);
            for (int i = (startPosition + 1) % Capacity; i != followingBlockPosition; i = ++i % Capacity)
            {
                Array.Copy(Storage[i], 0, Storage[startPosition], currentCount, Storage[i].Length);
                currentCount += Storage[i].Length;
                DecreaseCount();   // Количество блоков в буфере фактически уменьшается
            }
            // Сдвиг остальных блоков
            WritePosition = (WritePosition + Capacity - (numberOfBlocks - 1)) % Capacity;
            for (int i = (startPosition + 1) % Capacity; i != WritePosition; i = ++i % Capacity)
            {
                Storage[i] = Storage[(i + 1) % Capacity];
            }
        }
        // Метод присваивает всем блокам в заданных границах, которые не равны null значение null.
        // При этом уменьшается счётчик блоков буфера.
        internal void DeleteBlocks(int startPosition, int endPosition)
        {
            if ((startPosition < 0 && startPosition >= Capacity) || (endPosition < 0 && endPosition >= Capacity))
            {
                throw new ArgumentOutOfRangeException();
            }
            for (int i = startPosition; i != endPosition % Capacity; i = ++i % Capacity)
            {
                lock (BlockLockers[i])
                {
                    if (Storage[i] != null)
                    {
                        Storage[i] = null;
                        DecreaseCount();
                    }
                }
            }
        }
    }
}
