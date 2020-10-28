using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GzipArchiver
{
    class Reader : FileUser
    {
        private const int _sizeOfDataBlock = 1024 * 1024;
        private const int _maxArrayIndex = 0x7fffffc7; //According to documentation the byte array is limited to a maximum index of 0X7FFFFFC7 in any given dimension.
        private int _defectedBlockNumber;
        private object _defectedBlockNumberLocker = new object();
        private int _isizeMismatchedBlockNumber;
        private object _isizeMismatchedBlockNumberLocker = new object();

        internal event Action ReadyToWorkEvent;
        internal event Action<int> FixBufferEvent;

        internal Reader(string path, int numberOfBlocksPerPortion)
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            buffer = new PortionedCircularBuffer(numberOfBlocksPerPortion);
            _defectedBlockNumber = -1;
            _isizeMismatchedBlockNumber = -1;
        }

        internal int DefectedBlockNumber
        {
            get
            {
                return _defectedBlockNumber;
            }
            set
            {
                if (value < -1 || value >= buffer.Capacity)
                {
                    throw new ArgumentOutOfRangeException();
                }
                if (_defectedBlockNumber == -1 || value < _defectedBlockNumber)
                {
                    lock (_defectedBlockNumberLocker)
                    {
                        _defectedBlockNumber = value;
                    }
                }
            }
        }
        internal int IsizeMismatchedBlockNumber
        {
            get
            {
                return _isizeMismatchedBlockNumber;
            }
            set
            {
                if (value < -1 || value >= buffer.Capacity)
                {
                    throw new ArgumentOutOfRangeException();
                }
                if (_isizeMismatchedBlockNumber == -1 || value < _isizeMismatchedBlockNumber)
                {
                    lock (_isizeMismatchedBlockNumberLocker)
                    {
                        _isizeMismatchedBlockNumber = value;
                    }
                }
            }
        }

        internal void ReadUncompressedData()
        {
            // Запись в буфер производится порциями. Перед записью очередной
            // порции поток получает мониторы всех ячеек буфера, в которые будет
            // писать и отпускает мониторы всех ячеек предыдущей порции.
            for (int i = 0; i < buffer.PortionSize; i++)
            {
                buffer.AcquireBlockLock(i);
            }
            ReadPortionOfBlocks();

            ReadyToWorkEvent();

            bool readingIsOver = false;
            while (true)
            {
                if (buffer.Count == buffer.PortionSize)
                {
                    for (int i = 0; i < buffer.PortionSize; i++)
                    {
                        buffer.AcquireBlockLock(buffer.WritePosition + i);
                        buffer.ReleaseBlockLock((buffer.WritePosition + buffer.PortionSize + i) % buffer.Capacity);
                    }
                    if (fs.Length == fs.Position)
                    {
                        if (readingIsOver)
                        {
                            return;
                        }
                        readingIsOver = true;
                    }
                    ReadPortionOfBlocks();
                }
            }
        }
        private void ReadBlock()
        {
            byte[] block = new byte[_sizeOfDataBlock];
            fs.Read(block, 0, _sizeOfDataBlock);
            buffer.WriteBlock(ref block);
        }
        private void ReadDummyBlock()
        {
            byte[] block = new byte[0];
            buffer.WriteBlock(ref block);
        }
        private void ReadPortionOfBlocks()
        {
            for (int i = 0; i < buffer.PortionSize; i++)
            {
                if (fs.Length - fs.Position >= _sizeOfDataBlock)
                {
                    ReadBlock();
                }
                else if (fs.Length - fs.Position > 0)
                {
                    byte[] eofBlock = new byte[fs.Length - fs.Position];
                    fs.Read(eofBlock, 0, eofBlock.Length);
                    buffer.WriteBlock(ref eofBlock);
                }
                else
                {
                    ReadDummyBlock();
                }
            }
        }

        internal void ReadCompressedData()
        {
            // Запись в буфер производится порциями. Перед записью очередной
            // порции поток получает мониторы всех ячеек буфера, в которые будет
            // писать и отпускает мониторы всех ячеек предыдущей порции.
            byte[] remainder = new byte[0];

            for (int i = 0; i < buffer.PortionSize; i++)
            {
                buffer.AcquireBlockLock(i);
            }
            ReadPortionOfGzipMembers(ref remainder);

            ReadyToWorkEvent();

            bool readingIsOver = false;
            while (true)
            {
                if (buffer.Count == buffer.PortionSize)
                {
                    for (int i = 0; i < buffer.PortionSize; i++)
                    {
                        buffer.AcquireBlockLock(buffer.WritePosition + i);
                        buffer.ReleaseBlockLock((buffer.WritePosition + buffer.PortionSize + i) % buffer.Capacity);
                    }

                    // Если в данной части буфера был обнаружен дефектный блок, то нужно проверить причину его появления.
                    if ((DefectedBlockNumber >= buffer.WritePosition) && (DefectedBlockNumber < buffer.WritePosition + buffer.PortionSize))
                    {
                        if (IsizeMismatchedBlockNumber == -1 || DefectedBlockNumber < IsizeMismatchedBlockNumber)    // Если дефект не обусловлен ошибкой при определении границ члена gzip (gzip member), то
                        {
                            Console.WriteLine("Ошибка при попытке декомпрессии очередного gzip member. Недопустимый формат данных. Программа будет закрыта.");
                            Console.ReadKey(true);
                            Environment.Exit(1);
                        }
                    }
                    // Если в данной части буфера было выявлено несовпадение размера блока после декомпрессии c ISIZE.
                    if ((IsizeMismatchedBlockNumber >= buffer.WritePosition) && (IsizeMismatchedBlockNumber < buffer.WritePosition + buffer.PortionSize))
                    {
                        FixBuffer(ref remainder);
                        continue;
                    }

                    if (fs.Length == fs.Position)
                    {
                        if (readingIsOver)
                        {
                            return;
                        }
                        readingIsOver = true;
                    }
                    ReadPortionOfGzipMembers(ref remainder);
                }
            }
        }
        private void ReadPortionOfGzipMembers(ref byte[] remainder)
        {
            for (int i = 0; i < buffer.PortionSize; i++)
            {
                if (fs.Position < fs.Length || remainder.Length != 0)
                {
                    ReadGzipMember(ref remainder);
                }
                else
                {
                    ReadDummyBlock();
                }
            }
        }
        private void ReadGzipMember(ref byte[] remainder)
        {
            int endOfMemberPosition;
            byte[] data = new byte[remainder.Length];
            Array.Copy(remainder, data, remainder.Length);
            for (int i = 0; true; i++)
            {
                endOfMemberPosition = SearchEndOfGzipMember(data);
                if (endOfMemberPosition != -1)
                {
                    break;
                }

                if (data.Length == _maxArrayIndex + 1)
                {
                    Console.WriteLine("Программа не смогла найти очередной gzip member в исходном фале и будет закрыта.");
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
                if (fs.Length - fs.Position >= _sizeOfDataBlock)
                {
                    newLength = (uint)(data.Length + _sizeOfDataBlock);
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

                int currentCount = data.Length;
                Array.Resize(ref data, (int)newLength);
                fs.Read(data, currentCount, data.Length - currentCount);
            }
            remainder = new byte[data.Length - endOfMemberPosition];
            Array.Copy(data, endOfMemberPosition, remainder, 0, remainder.Length);
            Array.Resize(ref data, endOfMemberPosition);
            buffer.WriteBlock(ref data);
        }
        private int SearchEndOfGzipMember(byte[] arrayStartedWithGzipMember)
        {
            int searchStartPosition = 10;   // Первые 10 байтов занимает хэдер.
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
        private void FixBuffer(ref byte[] remainder)
        {
            while (buffer.Count != 0) { }   // Потоки декомпресси должны закончить работу и находиться в состоянии ожидания.

            for (int i = 0; i < buffer.PortionSize; i++)
            {
                buffer.AcquireBlockLock((buffer.WritePosition + buffer.PortionSize + i) % buffer.Capacity);
            }

            // Ремонт буфера-источника.
            buffer.MergeBlocks(IsizeMismatchedBlockNumber, 2);   // Слияние двух блоков и сдвиг остальных.
            if (fs.Position < fs.Length || remainder.Length != 0)
            {
                ReadGzipMember(ref remainder);  // Запсись нового блока на освободившееся место.
            }
            else
            {
                ReadDummyBlock();
            }

            FixBufferEvent(buffer.WritePosition);   // Ремонт тагрет буфера и восстановление флагов GzipOperator.

            DefectedBlockNumber = -1;
            IsizeMismatchedBlockNumber = -1;

            for (int i = 0; i < buffer.PortionSize; i++)
            {
                buffer.ReleaseBlockLock(buffer.WritePosition + i);
            }
        }
    }
}
