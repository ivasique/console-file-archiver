using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GzipArchiver
{
    class Writer : FileUser
    {
        private int _blockCount = 0;  // Это поле используется для визуализации в консоли (для красоты).

        internal event Action ReadyToWorkEvent;

        internal Writer(string path, int numberOfBlocksInPortion)
        {
            fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            buffer = new PortionedCircularBuffer(numberOfBlocksInPortion);
        }

        internal void WriteResult()
        {
            // Запись из буфера в файл производится порциями. Перед чтением из буфера очередной
            // порции поток получает мониторы всех ячеек буфера, из которых будет
            // читать и отпускает мониторы всех ячеек предыдущей считанной порции.
            for (int i = buffer.PortionSize; i < buffer.PortionSize * 2; i++)
            {
                buffer.AcquireBlockLock(i);
            }

            ReadyToWorkEvent();

            while (true)
            {
                if (buffer.Count == buffer.PortionSize)
                {
                    for (int i = 0; i < buffer.PortionSize; i++)
                    {
                        buffer.AcquireBlockLock(i + buffer.ReadPosition);
                        buffer.ReleaseBlockLock((i + buffer.ReadPosition + buffer.PortionSize) % buffer.Capacity);
                    }
                    for (int i = 0; i < buffer.PortionSize; i++)
                    {
                        if (buffer.Peek() == null)
                        {
                            Console.WriteLine("\r" + "Processing...".PadRight(++_blockCount % 24, '.').PadRight(40));   // Визуализация в консоли.
                            return;
                        }
                        WriteBlock();
                    }
                    Console.Write("\r" + "Processing".PadRight(++_blockCount % 24, '.').PadRight(40));   // Визуализация в консоли.
                }
            }
        }
        private void WriteBlock()
        {
            byte[] block = buffer.ReadBlock();
            fs.Write(block, 0, block.Length);
        }
        internal void FixBuffer(int startPosition)
        {
            buffer.DeleteBlocks(startPosition, (startPosition + buffer.PortionSize) % buffer.Capacity);
        }
    }
}
