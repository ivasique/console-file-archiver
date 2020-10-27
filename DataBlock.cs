using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
    class DataBlock
    {
        private int _blockNumber;
        internal byte[] Data;

        internal DataBlock(int blockNumber)
        {
            _blockNumber = blockNumber;
            Data = null;
        }
        internal DataBlock(int blockNumber, byte[] data)
        {
            _blockNumber = blockNumber;
            Data = (byte[])data.Clone();
        }
        internal DataBlock(int blockNumber, ref byte[] data)
        {
            _blockNumber = blockNumber;
            Data = data;
            data = null;
        }

        internal int BlockNumber
        {
            get { return _blockNumber; }
        }
    }
}
