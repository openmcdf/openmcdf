using Microsoft.IO;

namespace OpenMcdf.Tests;

[TestClass]
public sealed class RootStorageTests
{
    [TestMethod]
    [DoNotParallelize] // Test sharing
    [DataRow("TestStream_v3_0.cfs")]
    public void Open(string fileName)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        using var rootStorage2 = RootStorage.OpenRead(fileName);

        Assert.ThrowsExactly<IOException>(() => RootStorage.Open(fileName, FileMode.Open));
        Assert.ThrowsExactly<IOException>(() => RootStorage.Open(fileName, FileMode.Open, FileAccess.ReadWrite));

        using CfbStream stream = rootStorage.OpenStream("TestStream");
        Assert.ThrowsExactly<NotSupportedException>(() => stream.WriteByte(0));

        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.CreateStream("TestStream2"));
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.CreateStorage("TestStream2"));
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.Delete("TestStream"));
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.Commit());
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.Revert());
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.CreationTime = DateTime.MinValue);
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.ModifiedTime = DateTime.MinValue);
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.CLSID = Guid.Empty);
        Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.StateBits = 0);
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void ConsolidateMemoryStream(Version version)
    {
        byte[] buffer = new byte[4096];

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using (CfbStream stream = rootStorage.CreateStream("Test"))
                stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(1, rootStorage.EnumerateEntries().Count());

            rootStorage.Flush(true);

            int originalMemoryStreamLength = (int)memoryStream.Length;

            rootStorage.Delete("Test");

            rootStorage.Flush(true);

            Assert.IsGreaterThan(memoryStream.Length, originalMemoryStreamLength);
        }

        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            Assert.AreEqual(0, rootStorage.EnumerateEntries().Count());
        }
    }

    [TestMethod]
    [DataRow(Version.V3, StorageModeFlags.None)]
    [DataRow(Version.V4, StorageModeFlags.Transacted)]
    public void ConsolidateFile(Version version, StorageModeFlags flags)
    {
        byte[] buffer = new byte[4096];

        string fileName = Path.GetTempFileName();

        try
        {
            using (var rootStorage = RootStorage.Create(fileName, version, flags))
            {
                using (CfbStream stream = rootStorage.CreateStream("Test"))
                    stream.Write(buffer, 0, buffer.Length);

                Assert.AreEqual(1, rootStorage.EnumerateEntries().Count());

                if (flags.HasFlag(StorageModeFlags.Transacted))
                    rootStorage.Commit();
                rootStorage.Flush(true);

                long originalLength = new FileInfo(fileName).Length;

                rootStorage.Delete("Test");

                if (flags.HasFlag(StorageModeFlags.Transacted))
                    rootStorage.Commit();
                rootStorage.Flush(true);

                long consolidatedLength = new FileInfo(fileName).Length;
                Assert.IsGreaterThan(consolidatedLength, originalLength);
            }

            using (var rootStorage = RootStorage.OpenRead(fileName))
            {
                Assert.AreEqual(0, rootStorage.EnumerateEntries().Count());
            }
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    [DataRow(Version.V3, 0)]
    [DataRow(Version.V3, 1)]
    [DataRow(Version.V3, 2)]
    [DataRow(Version.V3, 4)] // Required 2 sectors including root
    [DataRow(Version.V4, 0)]
    [DataRow(Version.V4, 1)]
    [DataRow(Version.V4, 2)]
    [DataRow(Version.V4, 32)] // Required 2 sectors including root
    public void SwitchStream(Version version, int subStorageCount)
    {
        using MemoryStream memoryStream = new();
        using MemoryStream switchedMemoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            for (int i = 0; i < subStorageCount; i++)
                rootStorage.CreateStorage($"Test{i}");

            rootStorage.SwitchTo(switchedMemoryStream);
        }

        memoryStream.Position = 0;
        using (var rootStorage = RootStorage.Open(switchedMemoryStream, StorageModeFlags.LeaveOpen))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.AreEqual(subStorageCount, entries.Count());

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{i}");
        }
    }

    [TestMethod]
    [DataRow(Version.V3, 0)]
    [DataRow(Version.V3, 1)]
    [DataRow(Version.V3, 2)]
    [DataRow(Version.V3, 4)] // Required 2 sectors including root
    [DataRow(Version.V4, 0)]
    [DataRow(Version.V4, 1)]
    [DataRow(Version.V4, 2)]
    [DataRow(Version.V4, 32)] // Required 2 sectors including root
    public void SwitchToFile(Version version, int subStorageCount)
    {
        string fileName = Path.GetTempFileName();

        try
        {
            using (var rootStorage = RootStorage.CreateInMemory(version))
            {
                for (int i = 0; i < subStorageCount; i++)
                    rootStorage.CreateStorage($"Test{i}");

                rootStorage.SwitchTo(fileName);
            }

            using (var rootStorage = RootStorage.OpenRead(fileName))
            {
                IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
                Assert.AreEqual(subStorageCount, entries.Count());

                for (int i = 0; i < subStorageCount; i++)
                    rootStorage.OpenStorage($"Test{i}");
            }
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    [DataRow(Version.V3, 0)]
    [DataRow(Version.V3, 1)]
    [DataRow(Version.V3, 2)]
    [DataRow(Version.V3, 4)]
    [DataRow(Version.V4, 0)]
    [DataRow(Version.V4, 1)]
    [DataRow(Version.V4, 2)]
    [DataRow(Version.V4, 4)]
    public void SwitchToWritableStream(Version version, int streamCount)
    {
        string fileName = Path.GetTempFileName();

        var data = TestData.CreateByteArray(1024);

        try
        {
            using (var rootStorage = RootStorage.CreateInMemory(version))
            {
                for (int i = 0; i < streamCount; i++)
                {
                    using CfbStream stream = rootStorage.CreateStream($"Test{i}");
                    stream.Write(data, 0, data.Length);
                }

                rootStorage.SwitchTo(fileName);
            }

            using MemoryStream memoryStream = new();
            using (var rootStorage = RootStorage.OpenRead(fileName))
            {
                rootStorage.SwitchTo(memoryStream);

                IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
                Assert.AreEqual(streamCount, entries.Count());

                for (int i = 0; i < streamCount; i++)
                    rootStorage.Delete($"Test{i}");
            }
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    [DataRow(Version.V3, 0)]
    [DataRow(Version.V3, 1)]
    [DataRow(Version.V3, 2)]
    [DataRow(Version.V3, 4)] // Required 2 sectors including root
    [DataRow(Version.V4, 0)]
    [DataRow(Version.V4, 1)]
    [DataRow(Version.V4, 2)]
    [DataRow(Version.V4, 32)] // Required 2 sectors including root
    public void SwitchTransactedStream(Version version, int subStorageCount)
    {
        using MemoryStream originalMemoryStream = new();
        using MemoryStream switchedMemoryStream = new();

        using (var rootStorage = RootStorage.Create(originalMemoryStream, version, StorageModeFlags.Transacted | StorageModeFlags.LeaveOpen))
        {
            for (int i = 0; i < subStorageCount; i++)
                rootStorage.CreateStorage($"Test{i}");

            rootStorage.SwitchTo(switchedMemoryStream);
            rootStorage.Commit();
        }

        using (var rootStorage = RootStorage.Open(switchedMemoryStream))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.AreEqual(subStorageCount, entries.Count());

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{i}");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeleteTrimsBaseStream(bool consolidate)
    {
        using var rootStorage = RootStorage.CreateInMemory(Version.V3);
        using (CfbStream stream = rootStorage.CreateStream("Test"))
        {
            byte[] buffer = TestData.CreateByteArray(4096);
            stream.Write(buffer, 0, buffer.Length);
        }

        rootStorage.Flush(consolidate);

        long originalLength = rootStorage.BaseStream.Length;

        rootStorage.Delete("Test");
        rootStorage.Flush(consolidate);

        long newLength = rootStorage.BaseStream.Length;

        Assert.IsGreaterThan(newLength, originalLength);
    }

    [TestMethod]
    [DoNotParallelize] // High memory usage
    public void V3ThrowsIOExceptionAt2GB()
    {
        const long MaxStreamLength = 2L * 1024 * 1024 * 1024;

        RecyclableMemoryStreamManager manager = new();
        using RecyclableMemoryStream baseStream = new(manager);
        baseStream.Capacity64 = MaxStreamLength;

        using var rootStorage = RootStorage.Create(baseStream, Version.V3);
        using CfbStream stream = rootStorage.CreateStream("Test");
        byte[] buffer = TestData.CreateByteArray(1024 * 1024);
        while (baseStream.Length + buffer.Length <= MaxStreamLength)
            stream.Write(buffer, 0, buffer.Length);

        Assert.ThrowsExactly<IOException>(() => stream.Write(buffer, 0, buffer.Length));
    }

    [TestMethod]
    [DoNotParallelize] // High memory usage
    public void ValidateRangeLockSector()
    {
        RecyclableMemoryStreamManager manager = new();
        using RecyclableMemoryStream baseStream = new(manager);
        baseStream.Capacity64 = RootContext.RangeLockSectorOffset;

        using var rootStorage = RootStorage.Create(baseStream, Version.V4);
        using (CfbStream stream = rootStorage.CreateStream("Test"))
        {
            byte[] buffer = TestData.CreateByteArray(4096);
            while (baseStream.Length <= RootContext.RangeLockSectorOffset)
                stream.Write(buffer, 0, buffer.Length);
        }

        Assert.IsTrue(rootStorage.Validate());

        rootStorage.Delete("Test");
        rootStorage.Flush();

        Assert.IsTrue(rootStorage.Validate());
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnOpen()
    {
        using var root = RootStorage.OpenRead("DirectoryTreeCycle.cfb");
        Assert.ThrowsExactly<FileFormatException>(() => root.TryOpenStorage("AB", out _));
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnEnumerate()
    {
        using var root = RootStorage.OpenRead("DirectoryTreeCycle.cfb");
        var enumerator = root.EnumerateEntries().GetEnumerator();
        Assert.ThrowsExactly<FileFormatException>(() =>
        {
            while (enumerator.MoveNext())
            {
                // No-op
            }
        });
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnCreate()
    {
        using MemoryStream stream = TestData.CreateMemoryStreamFromFile("DirectoryTreeCycle.cfb");
        using var root = RootStorage.Open(stream);
        Assert.ThrowsExactly<FileFormatException>(() => root.CreateStorage("AB"));
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnDelete()
    {
        using MemoryStream stream = TestData.CreateMemoryStreamFromFile("DirectoryTreeCycle.cfb");
        using var root = RootStorage.Open(stream);
        Assert.ThrowsExactly<FileFormatException>(() => root.Delete("AB"));
    }
}
