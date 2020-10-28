using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GzipArchiver
{
    class GzipOperator
    {
        private PortionedCircularBuffer _sourceBuf;
        private PortionedCircularBuffer _targetBuf;
        static private int _defectedBlockNumber;
        static private object _defectedBlockNumberLocker = new object();
        static private int _isizeMismatchedBlockNumber;
        static private object _isizeMismatchedBlockNumberLocker = new object();

        internal event Action<int> FoundDefectEvent;
        internal event Action<int> FoundIsizeMismatchEvent;

        internal GzipOperator(PortionedCircularBuffer sourceBuf, PortionedCircularBuffer targetBuf)
        {
            if (sourceBuf == null || targetBuf == null)
            {
                throw new ArgumentNullException();
            }
            this._sourceBuf = sourceBuf;
            this._targetBuf = targetBuf;
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
                if (value < -1 || value >= _sourceBuf.Capacity)
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
                if (value < -1 || value >= _sourceBuf.Capacity)
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

        internal void Compress(object portionBlockNumber)
        {
            int bufferBlockNumber = (int)portionBlockNumber;
            while (true)
            {
                _sourceBuf.AcquireBlockLock(bufferBlockNumber);
                if (_sourceBuf.Peek(bufferBlockNumber).Length == 0) // Если поток встретил блок нулевой длины,
                {                                                   // то он завершает работу
                    _sourceBuf.DecreaseCount();
                    _sourceBuf.ReleaseBlockLock(bufferBlockNumber);
                    _targetBuf.AcquireBlockLock(bufferBlockNumber);
                    _targetBuf.IncreaseCount();
                    _targetBuf.ReleaseBlockLock(bufferBlockNumber);
                    return;
                }
                CompressBlock(bufferBlockNumber);
                _sourceBuf.ReleaseBlockLock(bufferBlockNumber);
                bufferBlockNumber = (bufferBlockNumber + _sourceBuf.PortionSize) % _sourceBuf.Capacity; // Переход к следующей порции блоков
            }
        }
        private void CompressBlock(int bufferBlockNumber)
        {
            using (MemoryStream compressedMemStream = new MemoryStream())
            {
                using (GZipStream gzipStream = new GZipStream(compressedMemStream, CompressionMode.Compress))
                {
                    byte[] uncompressedBlock = _sourceBuf.ReadBlock(bufferBlockNumber);
                    gzipStream.Write(uncompressedBlock, 0, uncompressedBlock.Length);
                }
                _targetBuf.AcquireBlockLock(bufferBlockNumber);
                byte[] compressedBlock = compressedMemStream.ToArray();
                _targetBuf.WriteBlock(ref compressedBlock, bufferBlockNumber);
                _targetBuf.ReleaseBlockLock(bufferBlockNumber);
            }
        }
        internal void Decompress(object portionBlockNumber)
        {
            int bufferBlockNumber = (int)portionBlockNumber;
            while (true)
            {
                _sourceBuf.AcquireBlockLock(bufferBlockNumber);

                if ( (IsizeMismatchedBlockNumber != -1) &&   // Если после расжатия блоков предыдущей порции выявлено несовпадение размера блока с указанным в ISIZE
                    !( (IsizeMismatchedBlockNumber >= (bufferBlockNumber / _sourceBuf.PortionSize) * _sourceBuf.PortionSize) &&
                        (IsizeMismatchedBlockNumber < (bufferBlockNumber + _sourceBuf.PortionSize) / _sourceBuf.PortionSize * _sourceBuf.PortionSize) ) )
                {
                    _sourceBuf.DecreaseCount();     // Чтение "в холостую"
                }
                else
                {
                    if (_sourceBuf.Peek(bufferBlockNumber).Length == 0) // Если поток встретил блок нулевой длины,
                    {                                                   // то он завершает работу
                        _sourceBuf.DecreaseCount();
                        _sourceBuf.ReleaseBlockLock(bufferBlockNumber);
                        _targetBuf.AcquireBlockLock(bufferBlockNumber);
                        byte[] nullBlock = null;
                        _targetBuf.WriteBlock(ref nullBlock, bufferBlockNumber);
                        _targetBuf.ReleaseBlockLock(bufferBlockNumber);
                        return;
                    }
                    DecompressBlock(bufferBlockNumber);
                }
                _sourceBuf.ReleaseBlockLock(bufferBlockNumber);
                bufferBlockNumber = (bufferBlockNumber + _sourceBuf.PortionSize) % _sourceBuf.Capacity; // Переход к следующей порции блоков
            }
        }
        private void DecompressBlock(int blockNumberInBuffer)
        {
            using (MemoryStream compressedMemStream = new MemoryStream())
            {
                compressedMemStream.Write(_sourceBuf.Peek(blockNumberInBuffer), 0, _sourceBuf.Peek(blockNumberInBuffer).Length);
                compressedMemStream.Position = 0;
                using (GZipStream decompressGzipStream = new GZipStream(compressedMemStream, CompressionMode.Decompress))
                {
                    using (MemoryStream uncompressedMemStream = new MemoryStream())
                    {
                        try
                        {
                            decompressGzipStream.CopyTo(uncompressedMemStream);
                        }
                        catch (InvalidDataException e)
                        {
                            DefectedBlockNumber = blockNumberInBuffer;
                            FoundDefectEvent(DefectedBlockNumber);
                            _sourceBuf.DecreaseCount();
                            return;
                        }
                        // ISIZE is the size of the original (uncompressed) input data. ISIZE is stored in the last four bytes of each gzip member.
                        int ISIZE = BitConverter.ToInt32(_sourceBuf.Peek(blockNumberInBuffer), _sourceBuf.Peek(blockNumberInBuffer).Length - 4);
                        if (uncompressedMemStream.Length != ISIZE)
                        {
                            IsizeMismatchedBlockNumber = blockNumberInBuffer;
                            FoundIsizeMismatchEvent(blockNumberInBuffer);
                            _sourceBuf.DecreaseCount();
                            return;
                        }
                        _sourceBuf.DecreaseCount();
                        _targetBuf.AcquireBlockLock(blockNumberInBuffer);
                        byte[] block = uncompressedMemStream.ToArray();
                        _targetBuf.WriteBlock(ref block, blockNumberInBuffer);
                        _targetBuf.ReleaseBlockLock(blockNumberInBuffer);
                    }
                }
            }
        }
    }
}
