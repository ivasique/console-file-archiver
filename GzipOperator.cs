using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    class GzipOperator
    {
        static private List<DataBlock> _blocksWithIsizeMismatch = new List<DataBlock>();

        private NumberedQueue _sourceBuffer;
        private NumberedQueue _targetBuffer;

        internal GzipOperator(NumberedQueue sourceBuffer, NumberedQueue targetBuffer)
        {
            _sourceBuffer = sourceBuffer;
            _targetBuffer = targetBuffer;
        }

        internal void Compress()
        {
            while (true)
            {
                DataBlock block = _sourceBuffer.ReadBlock();
                if (block.Data == null)
                {
                    DataBlock emptyBlock = new DataBlock(block.BlockNumber + 1);
                    _sourceBuffer.WriteBlock(ref emptyBlock);   // Чтобы остальные gzip потоки тоже завершили работу.
                    _targetBuffer.WriteBlock(ref block);    // Чтобы оповестить Writer о том, что его работа окончена.
                    return;
                }
                using (MemoryStream compressedMemStream = new MemoryStream())
                {
                    using (GZipStream gzipStream = new GZipStream(compressedMemStream, CompressionMode.Compress))
                    {
                        gzipStream.Write(block.Data, 0, block.Data.Length);
                    }
                    block.Data = compressedMemStream.ToArray();
                }
                _targetBuffer.WriteBlock(ref block);
            }
        }
        internal void Decompress()
        {
            while (true)
            {
                DataBlock block = _sourceBuffer.ReadBlock();
                if (block.Data == null)
                {
                    DataBlock zeroLengthBlock = new DataBlock(block.BlockNumber + 1);
                    _sourceBuffer.WriteBlock(ref zeroLengthBlock);      // Чтобы остальные gzip потоки тоже завершили работу.
                    _targetBuffer.WriteBlock(ref block);        // Чтобы оповестить Writer о том, что его работа окончена.
                    return;
                }
                using (MemoryStream uncompressedMemStream = new MemoryStream())
                {
                    while (true)
                    {
                        using (MemoryStream compressedMemStream = new MemoryStream())
                        {
                            compressedMemStream.Write(block.Data, 0, block.Data.Length);
                            compressedMemStream.Position = 0;
                            using (GZipStream decompressGzipStream = new GZipStream(compressedMemStream, CompressionMode.Decompress))
                            {
                                try
                                {
                                    decompressGzipStream.CopyTo(uncompressedMemStream);
                                    break;
                                }
                                catch (InvalidDataException e)
                                {
                                    FixDefectedBlock(block);
                                }
                            }
                        }
                    }
                    // ISIZE is the size of the original (uncompressed) input data. ISIZE is stored in the last four bytes of each gzip member.
                    int ISIZE = BitConverter.ToInt32(block.Data, block.Data.Length - 4);
                    if (uncompressedMemStream.Length == ISIZE)  // Если размер расжатого блока совпал с ISIZE сжатого
                    {
                        block.Data = uncompressedMemStream.ToArray();
                        _targetBuffer.WriteBlock(ref block);

                        // Могу существовать потоки, которые обнаружили дефектный блок, но пока не смогли его обработать,
                        // так как не было ничего известно о предыдущем блоке. Если окажется, что блок предшествующий дефектному
                        // был расжат корректно (без обнауржения несоответствия ISIZE), то это свидельствует о недопустимом
                        // формате данных.
                        Monitor.Enter(_blocksWithIsizeMismatch);
                        Monitor.PulseAll(_blocksWithIsizeMismatch); // Уведомляются все потоки, которые обнаружили дефектные блоки.
                        Monitor.Exit(_blocksWithIsizeMismatch);
                        continue;
                    }
                    else    // Если размер расжатого блока НЕ совпал с ISIZE сжатого
                    {
                        Monitor.Enter(_blocksWithIsizeMismatch);
                        _blocksWithIsizeMismatch.Add(block);        // Заношу текущий блок в специальный список.
                        Monitor.PulseAll(_blocksWithIsizeMismatch); // Оповещаю всех читателей списка, что занесён новый блок.
                        Monitor.Exit(_blocksWithIsizeMismatch);
                    }
                }
            }
        }
        private void FixDefectedBlock(DataBlock block)
        {
            Monitor.Enter(_blocksWithIsizeMismatch);
            while (true)
            {
                if (block.BlockNumber == _targetBuffer.ExpectedBlockNumber)    // Если дефектный блок следующий в очереди записи.
                {
                    Console.WriteLine("Invalid data format. The application will be closed.");
                    Console.ReadKey(true);
                    Environment.Exit(1);
                }
                DataBlock previousIsizeMismatchBlock = null;
                foreach (var isizeMismatchBlock in _blocksWithIsizeMismatch)    // Поиск предыдущего блока в списке блоков с несовпадением ISIZE.
                {
                    if (block.BlockNumber - 1 == isizeMismatchBlock.BlockNumber)
                    {
                        previousIsizeMismatchBlock = isizeMismatchBlock;
                        _blocksWithIsizeMismatch.Remove(isizeMismatchBlock);
                        DataBlock emptyBlock = new DataBlock(previousIsizeMismatchBlock.BlockNumber, new byte[0]);
                        _targetBuffer.WriteBlock(ref emptyBlock);
                        Monitor.Exit(_blocksWithIsizeMismatch);
                        break;
                    }
                }
                if (previousIsizeMismatchBlock != null) // Если в списке блоков с несовпадением ISIZE был найден предыдущий блок
                {
                    // Совмещаю два блок в один.
                    byte[] fixedBlockData = new byte[previousIsizeMismatchBlock.Data.Length + block.Data.Length];
                    Array.Copy(previousIsizeMismatchBlock.Data, fixedBlockData, previousIsizeMismatchBlock.Data.Length);
                    Array.Copy(block.Data, 0, fixedBlockData, previousIsizeMismatchBlock.Data.Length, block.Data.Length);
                    block.Data = fixedBlockData; 
                    break;
                }
                else
                {
                    Monitor.Wait(_blocksWithIsizeMismatch);
                    continue;
                }
            }
        }
    }
}
