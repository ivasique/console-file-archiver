using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    class Reader : FileUser
    {
        private const int _sizeOfBlockData = 1024 * 1024;
        private const int _maxArrayIndex = 0x7fffffc7; //According to documentation the byte array is limited to a maximum index of 0X7FFFFFC7 in any given dimension.
        private int _blockCount;

        internal Reader(string filePath, NumberedQueue buffer) : base(buffer)
        {
            fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _blockCount = 0;
        }

        internal void ReadUncompressedFile()
        {
            while ( fs.Length - fs.Position > _sizeOfBlockData )
            {
                ReadBlock(_sizeOfBlockData);
            }
            if (fs.Length - fs.Position > 0)
            {
                ReadBlock((int)(fs.Length - fs.Position));
            }
            DataBlock block = new DataBlock(_blockCount); // Блок без данных используется в качестве флага конца файла.
            buffer.WriteBlock(ref block);
            _blockCount++;   
        }
        internal void ReadBlock(int blockDataSize)
        {
            byte[] blockData = new byte[blockDataSize];
            fs.Read(blockData, 0, blockDataSize);
            DataBlock block = new DataBlock(_blockCount, ref blockData);
            buffer.WriteBlock(ref block);
            _blockCount++;
        }
        internal void ReadCompressedFile()
        {
            byte[] remainder = new byte[0];
            while (remainder.Length != 0 || fs.Length != fs.Position)
            {
                ReadGzipMember(ref remainder);
            }
            DataBlock block = new DataBlock(_blockCount); // Блок без данных используется в качестве флага конца файла.
            buffer.WriteBlock(ref block);
            _blockCount++;
        }
        private void ReadGzipMember(ref byte[] remainder)
        {
            int endOfMemberPosition;
            int currentCount = 0;
            byte[] data = remainder;
            for (int i = 0; true; i++)
            {
                endOfMemberPosition = SearchEndOfGzipMember(data, currentCount - 2);    // Вычитается 2, чтобы не пропустить сигнатуру на стыке двух считанных порций байтов.
                if (endOfMemberPosition != -1)
                {
                    break;
                }
                currentCount = data.Length;
                if (data.Length == _maxArrayIndex + 1)
                {
                    Console.WriteLine("A gzip member could not be found. The application will be closed.");
                    Console.ReadKey(true);
                    Environment.Exit(1);
                }
                //// Можно предусмотреть проверку, предотвращающую возможное переполнение оперативной памяти.
                //PerformanceCounter ramcounter = new PerformanceCounter("memory", "available mbytes");
                //if (data.Length >= ramcounter.NextValue() * 1024 * 1024)
                //{
                //    Console.WriteLine("Программа не смогла найти очередной gzip member в исходном фале по причине переполнения RAM и будет закрыта.");
                //    Console.ReadKey(true);
                //    Environment.Exit(1);
                //}

                uint newLength;
                if (fs.Length - fs.Position >= _sizeOfBlockData)
                {
                    newLength = (uint)(data.Length + _sizeOfBlockData);
                }
                else if (fs.Position < fs.Length)
                {
                    newLength = (uint)(data.Length + (int)(fs.Length - fs.Position));
                }
                else  // inputStream.Position == inputStream.Length
                {
                    endOfMemberPosition = data.Length;
                    break;
                }

                if (newLength > _maxArrayIndex + 1) { newLength = _maxArrayIndex + 1; }
                //if (newLength > ramCounter.NextValue()*1024*1024) { newLength = (int)ramCounter.NextValue()*1024*1024; }  // Проверка на переполнение оперативной памяти.

                Array.Resize(ref data, (int)newLength);
                fs.Read(data, currentCount, data.Length - currentCount);
            }
            remainder = new byte[data.Length - endOfMemberPosition];
            Array.Copy(data, endOfMemberPosition, remainder, 0, remainder.Length);
            Array.Resize(ref data, endOfMemberPosition);
            DataBlock block = new DataBlock(_blockCount, ref data);
            buffer.WriteBlock(ref block);
            _blockCount++;
        }
        private int SearchEndOfGzipMember(byte[] arrayStartedWithGzipMember, int searchStartPosition)
        {
            if (searchStartPosition < 10)
            {
                searchStartPosition = 10;   // Первые 10 байтов занимает хэдер.
            } 
            const byte ID1 = 0x1f;  // Любой модуль gzip файла начинается с хэдера.
            const byte ID2 = 0x8b;  // Первые три бита (ID1, ID2 и СМ соответственно хэдера постоянны для любого gzip модуля.
            const byte CM = 0x08;   // Подробную информацию можно найти в "GZIP file format specification version 4.3."
            for (int i = searchStartPosition; i <= arrayStartedWithGzipMember.Length - 3; i++)
            {
                if ((arrayStartedWithGzipMember[i] == ID1) && (arrayStartedWithGzipMember[i + 1] == ID2) && (arrayStartedWithGzipMember[i + 2] == CM))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}

