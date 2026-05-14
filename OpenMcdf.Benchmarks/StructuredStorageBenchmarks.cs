using StructuredStorage;

namespace OpenMcdf.Benchmarks;

internal static class StructuredStorageBenchmarks
{
    public static void ReadStream(string fileName, byte[] buffer)
    {
        using var storage = StructuredStorage.Storage.Open(fileName);
        using StructuredStorage.Stream storageStream = storage.OpenStream("Test");

        long length = storageStream.Length; // Length lookup is expensive
        long totalRead = 0;
        while (totalRead < length)
        {
            int read = storageStream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                throw new EndOfStreamException($"Read past end of stream at {storageStream.Position}/{storageStream.Length}.");
            totalRead += read;
        }
    }

    public static void ReadStream(MemoryStream stream, byte[] buffer)
    {
        using var storage = StructuredStorage.Storage.Open(stream);
        using StructuredStorage.Stream storageStream = storage.OpenStream("Test");

        long length = storageStream.Length; // Length lookup is expensive
        long totalRead = 0;
        while (totalRead < length)
        {
            int read = storageStream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                throw new EndOfStreamException($"Read past end of stream at {storageStream.Position}/{storageStream.Length}.");
            totalRead += read;
        }
    }

    public static void WriteStream(string fileName, Version version, StorageModeFlags flags, byte[] buffer, long streamLength)
    {
        File.Delete(fileName);

        StorageModes modes = StorageModes.AccessReadWrite | StorageModes.ShareExclusive;
        if (flags.HasFlag(StorageModeFlags.Transacted))
            modes |= StorageModes.Transacted;

        using var storage = StructuredStorage.Storage.Create(fileName, modes, version == Version.V4);
        using StructuredStorage.Stream storageStream = storage.CreateStream("Test");

        long totalWritten = 0;
        while (totalWritten < streamLength)
        {
            storageStream.Write(buffer, 0, buffer.Length);
            totalWritten += buffer.Length;
        }
    }

    public static void WriteInMemory(Version version, StorageModeFlags flags, byte[] buffer, long streamLength)
    {
        if (version == Version.V4)
            throw new NotSupportedException();

        using var storage = StructuredStorage.Storage.CreateInMemory((int)streamLength * 2);
        using StructuredStorage.Stream storageStream = storage.CreateStream("Test");

        long totalWritten = 0;
        while (totalWritten < streamLength)
        {
            storageStream.Write(buffer, 0, buffer.Length);
            totalWritten += buffer.Length;
        }
    }
}
