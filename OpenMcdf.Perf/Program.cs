using System.Diagnostics;

namespace OpenMcdf.Perf;

internal sealed class Program
{
    static void Main(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        int bufferLength = 1024 * 1024;
        int streamLength = 8 * 1024 * 1024;
        Write(Version.V3, StorageModeFlags.None, bufferLength, streamLength, 100);
        Console.WriteLine($"Elapsed: {stopwatch.Elapsed}");
    }

    public static void Write(Version version, StorageModeFlags storageModeFlags, int bufferLength, int streamLength, int iterations)
    {
        byte[] buffer = new byte[bufferLength];

        // using FileStream baseStream = File.Create(Path.GetTempFileName());
        using MemoryStream baseStream = new(streamLength * iterations * 2);
        for (int i = 0; i < iterations; i++)
        {
            using var rootStorage = RootStorage.Create(baseStream, version, storageModeFlags | StorageModeFlags.LeaveOpen);
            using Stream stream = rootStorage.CreateStream("TestStream");

            for (int j = 0; j < streamLength / bufferLength; j++)
                stream.Write(buffer, 0, buffer.Length);

            if (storageModeFlags.HasFlag(StorageModeFlags.Transacted))
                rootStorage.Commit();
        }
    }

    public static void MultiStorageAndStreamWrite()
    {
        int storageCount = 8;
        int streamCount = 8;
        int writeCount = 1024;
        byte[] buffer = new byte[32 * 512];

        Microsoft.IO.RecyclableMemoryStreamManager manager = new();
        Microsoft.IO.RecyclableMemoryStream baseStream = new(manager);
        baseStream.Capacity = 2 * (storageCount * buffer.Length * writeCount + storageCount * (streamCount - 1) * buffer.Length);

        using var rootStorage = RootStorage.Create(baseStream, Version.V4);
        for (int k = 0; k < storageCount; k++)
        {
            Console.WriteLine($"Creating Storage {k}");
            Storage storage = rootStorage.CreateStorage($"TestStorage{k}");
            for (int i = 0; i < streamCount; i++)
            {
                using CfbStream stream = storage.CreateStream($"TestStream{i}");

                int to = i == 0 ? writeCount : 1;
                for (int j = 0; j < to; j++)
                    stream.Write(buffer, 0, buffer.Length);
            }
        }
    }

    public static void MultiStorageAndStreamWriteBaseline()
    {
        int storageCount = 8;
        int streamCount = 8;
        int writeCount = 1024;
        byte[] buffer = new byte[32 * 512];
        int capacity = 2 * (storageCount * buffer.Length * writeCount + storageCount * (streamCount - 1) * buffer.Length);

        using var rootStorage = StructuredStorage.Storage.CreateInMemory(capacity);
        for (int k = 0; k < storageCount; k++)
        {
            Console.WriteLine($"Creating Storage {k}");
            StructuredStorage.Storage storage = rootStorage.CreateStorage($"TestStorage{k}");
            for (int i = 0; i < streamCount; i++)
            {
                using StructuredStorage.Stream stream = storage.CreateStream($"TestStream{i}");

                int to = i == 0 ? writeCount : 1;
                for (int j = 0; j < to; j++)
                    stream.Write(buffer, 0, buffer.Length);
            }
        }
    }
}
