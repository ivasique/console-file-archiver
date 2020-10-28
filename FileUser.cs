using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GzipArchiver
{
    abstract class FileUser
    {
        internal PortionedCircularBuffer buffer;
        protected FileStream fs;

        ~FileUser()
        {
            try
            {
                if (fs != null)
                {
                    fs.Close();
                }
            }
            catch (ObjectDisposedException e) { }
        }
    }
}
