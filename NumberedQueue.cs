using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    // Отличается от классической очереди тем, что при записи блоков контролируется номер блока. Блоки могут быть записаны только строго по порядку.
    class NumberedQueue : Queue<DataBlock>
    {
        private int _capacity;  // Ограничиваю максимальный размер очереди, чтобы не перепорнялась 
                                // оперативная память в случае, когда диск работает быстрее процессора.
        private int _blockGlobalCount = 0;
        private object _readingMonitor = new object();
        private object _writingMonitor = new object();

        internal NumberedQueue(int capacity) : base(capacity)
        {
            _capacity = capacity;
        }

        internal int ExpectedBlockNumber
        {
            get { return _blockGlobalCount;  }
        }

        internal void WriteBlock(ref DataBlock block)
        {
            lock (_writingMonitor)
            {
                while (block.BlockNumber != ExpectedBlockNumber || Count == _capacity)  // При поытке записать блок с "неправильным" номером
                {                                                                       // или при переполнении очереди.
                    Monitor.Wait(_writingMonitor);
                }
                if (Count == 0) // Если очередь пуста, очевидно все потоки пытавшиеся читать были заблокированы.
                {
                    Monitor.Enter(_readingMonitor);
                    Enqueue(block);
                    Monitor.PulseAll(_readingMonitor);   // Уведомляем все ожидающие потоки, что из очереди снова можно читать.
                    Monitor.Exit(_readingMonitor);
                }
                else
                {
                    Enqueue(block);
                }
                _blockGlobalCount++;
                Monitor.PulseAll(_writingMonitor);   // Уведомляем потоки заблокированные из-за попытки записать блок с "неправильным" номером.
            }
            block = null;
        }
        internal DataBlock ReadBlock()
        {
            DataBlock block;
            lock (_readingMonitor)
            {
                if (Count == _capacity) // Если очередь заполнена, очевидно все потоки пытавшиеся произвести запись были заблокированы.
                {
                    Monitor.Enter(_writingMonitor);
                    block = TryDequeue();
                    Monitor.PulseAll(_writingMonitor);   // Уведомляем все ожидающие потоки, что в очередь снова можно писать.
                    Monitor.Exit(_writingMonitor);
                }
                else
                {
                    block = TryDequeue();
                }
            }
            return block;
        }
        private DataBlock TryDequeue()
        {
            while (true)
            {
                try
                {
                    return Dequeue();
                }
                catch (InvalidOperationException e) // Если очередь пуста,
                {
                    Monitor.Wait(_readingMonitor);   // то поток блокируется.
                }
            }
        }

    }
}
