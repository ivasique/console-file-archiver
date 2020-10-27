using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
    class Writer : FileUser
    {
        private int _blockCount = 0;  // Это поле используется для визуализации в консоли (для красоты).

        internal Writer(string filePath, NumberedQueue buffer) : base(buffer)
        {
            fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        }

        internal void WriteResultToFile()
        {
            while(true)
            {
                DataBlock block = buffer.ReadBlock();
                if (block.Data == null)
                {
                    Console.WriteLine("\r" + "Processing...".PadRight(++_blockCount % 20, '.').PadRight(40));   // Визуализация в консоли.
                    return;
                }
                fs.Write(block.Data, 0, block.Data.Length);
                Console.Write("\r" + "Processing".PadRight(++_blockCount % 20, '.').PadRight(40));   // Визуализация в консоли.
            }
        }
    }
}
