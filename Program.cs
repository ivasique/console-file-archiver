using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    class Program
    {
        private static int _numberOfGzipThreads = Environment.ProcessorCount;

        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Number of inputed arguments is wrong.");
                Console.WriteLine("Program takes three arguments: compress|decompress [name of source file] [name of target file].");   // Давать эту инфу?
                Console.ReadKey(true);
                return 1;
            }
            if (!File.Exists(args[1]))
            {
                Console.WriteLine("The source file does not exist.");
                Console.ReadKey(true);
                return 1;
            }
            else
            {
                FileInfo source = new FileInfo(args[1]);
                if (source.Length == 0)
                {
                    Console.WriteLine("The source file is zero-length file.");
                    Console.ReadKey(true);
                    return 1;
                }
            }
            string targetFilePath = args[2];
            while (File.Exists(targetFilePath))
            {
                Console.WriteLine("File \"" + args[2] + "\" alredy exists.");
                Console.Write("Press \"y\" to overwrite it or \"n\" to set a new target file path.");

                char desigion = ' ';
                while (!(desigion == 'y' || desigion == 'n'))
                {
                    desigion = Console.ReadKey(true).KeyChar;
                }
                Console.WriteLine();
                if (desigion == 'y')
                {
                    break;
                }
                else if (desigion == 'n')
                {
                    Console.Write("Set target file path: ");
                    targetFilePath = Console.ReadLine();
                }
            }
            switch (args[0])
            {
                case "compress":
                    return StartProcessing(CompressionMode.Compress, args[1], targetFilePath);
                case "decompress":
                    return StartProcessing(CompressionMode.Decompress, args[1], targetFilePath);
                default:
                    Console.WriteLine("The first argument passed to the program is out of range.");
                    Console.WriteLine("Argument equals \"{0}\", but it must be only \"compress\" or \"decompress\".", args[0]);
                    return 1;
            }
        }

        static private int StartProcessing(CompressionMode mode, string sourceFilePath, string targetFilePath)
        {
            NumberedQueue sourceBuffer = new NumberedQueue(_numberOfGzipThreads * 3);
            Reader reader = new Reader(sourceFilePath, sourceBuffer);
            Thread readingThread;
            switch (mode)
            {
                case CompressionMode.Compress:
                    readingThread = new Thread(reader.ReadUncompressedFile);
                    break;
                case CompressionMode.Decompress:
                    readingThread = new Thread(reader.ReadCompressedFile);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Parameter \"mode\" passed to " + System.Reflection.MethodInfo.GetCurrentMethod().Name + " method is out of range.");
            }
            readingThread.Start();

            NumberedQueue targetBuffer = new NumberedQueue(_numberOfGzipThreads * 2);
            Writer writer = new Writer(targetFilePath, targetBuffer);
            Thread writingThread = new Thread(writer.WriteResultToFile);
            writingThread.Start();

            GzipOperator[] gzipWorkers = new GzipOperator[_numberOfGzipThreads];
            Thread[] gzipThreads = new Thread[_numberOfGzipThreads];
            for (int i = 0; i < _numberOfGzipThreads; i++)
            {
                gzipWorkers[i] = new GzipOperator(sourceBuffer, targetBuffer);
                switch (mode)
                {
                    case CompressionMode.Compress:
                        gzipThreads[i] = new Thread(gzipWorkers[i].Compress);
                        break;
                    case CompressionMode.Decompress:
                        gzipThreads[i] = new Thread(gzipWorkers[i].Decompress);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("Parameter \"mode\" passed to " + System.Reflection.MethodInfo.GetCurrentMethod().Name + " method is out of range.");
                }
                gzipThreads[i].Start();
            }

            writingThread.Join();
            Console.WriteLine("Process completed successfully. Press any key to exit.");
            Console.ReadKey(true);
            return 0;
        }
    }
}
