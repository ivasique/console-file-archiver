using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
    abstract class FileUser
    {
        protected FileStream fs;
        protected NumberedQueue buffer;

        internal FileUser(NumberedQueue buffer)
        {
            this.buffer = buffer;
        }

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
