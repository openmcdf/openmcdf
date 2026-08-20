using Microsoft.IO;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf.Tests;

[TestClass]
public sealed class RootStorageTests
{
    [TestMethod]
    [DoNotParallelize] // Test sharing
    [DataRow("TestStream_v3_0.cfs")]
    public void Open(string fileName)
    {
        Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(nameof(fileName), FileMode.Create, FileAccess.Read));
        Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(nameof(fileName), FileMode.Open, FileAccess.Read, StorageModeFlags.Transacted));

        using var rootStorage = RootStorage.OpenRead(fileName);
        Assert.IsFalse(rootStorage.CanWrite);
        Assert.IsFalse(rootStorage.CanCommit);

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
    [DataRow("Corrupt.xls")]
    [SuppressMessage("Usage", "MSTEST0051:Assert.Throws should contain only a single statement/expression", Justification = "Multiple statements are required to open and validate the root storage.")]
    public void OpenWithStrictValidation(string fileName)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        bool valid = rootStorage.Validate();
        Assert.IsTrue(valid);

        Assert.ThrowsExactly<FileFormatException>(() =>
        {
            using var rootStorage = RootStorage.OpenRead(fileName, StorageModeFlags.StrictValidation);
            rootStorage.Validate();
        });
    }

    [TestMethod]
    public void OpenNonStrictWithNonZeroHeaderCLSID()
    {
        Guid expectedHeaderCLSID = Guid.Parse("00020906-0000-0000-c000-000000000046");
        using MemoryStream stream = TestData.CreateMemoryStreamFromFile("TestStream_v3_0.cfs");
        stream.Position = 8;
        stream.Write(expectedHeaderCLSID.ToByteArray(), 0, 16);
        stream.Position = 0;

        using (var rootStorage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen))
            Assert.AreEqual(expectedHeaderCLSID, rootStorage.HeaderCLSID);

        stream.Position = 0;
        Assert.ThrowsExactly<FileFormatException>(() =>
        {
            using var rootStorage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation);
        });
    }

    [TestMethod]
    public void SetHeaderCLSID()
    {
        Guid expectedHeaderCLSID = Guid.Parse("00020906-0000-0000-c000-000000000046");

        using MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, Version.V3, StorageModeFlags.LeaveOpen))
        {
            rootStorage.HeaderCLSID = expectedHeaderCLSID;
            Assert.AreEqual(expectedHeaderCLSID, rootStorage.HeaderCLSID);
        }

        stream.Position = 0;
        using (var rootStorage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen))
        {
            Assert.AreEqual(expectedHeaderCLSID, rootStorage.HeaderCLSID);
        }

        using MemoryStream strictStream = new();
        using var strictRootStorage = RootStorage.Create(strictStream, Version.V3, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => strictRootStorage.HeaderCLSID = expectedHeaderCLSID);
        strictRootStorage.HeaderCLSID = Guid.Empty;
        Assert.AreEqual(Guid.Empty, strictRootStorage.HeaderCLSID);
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

            Assert.HasCount(1, rootStorage.EnumerateEntries());

            rootStorage.Flush(true);

            int originalMemoryStreamLength = (int)memoryStream.Length;

            rootStorage.Delete("Test");

            rootStorage.Flush(true);

            Assert.IsGreaterThan(memoryStream.Length, originalMemoryStreamLength);
        }

        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            Assert.IsEmpty(rootStorage.EnumerateEntries());
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

                Assert.HasCount(1, rootStorage.EnumerateEntries());
                Assert.AreEqual(flags.HasFlag(StorageModeFlags.Transacted), rootStorage.CanCommit);

                if (rootStorage.CanCommit)
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

            using (var rootStorage = RootStorage.OpenRead(fileName, StorageModeFlags.StrictValidation))
            {
                Assert.IsEmpty(rootStorage.EnumerateEntries());
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
        using (var rootStorage = RootStorage.Open(switchedMemoryStream, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.HasCount(subStorageCount, entries);

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{i}");
        }
    }

    [TestMethod]
    public void SwitchStreamThrows()
    {
        using var rootStorage = RootStorage.CreateInMemory();
        using (FileStream createStream = File.Create("ReadOnly.cfb"))
        {
        }
        using FileStream stream = File.OpenRead("ReadOnly.cfb");
        Assert.ThrowsExactly<ArgumentException>(() => rootStorage.SwitchTo(stream));
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

            using (var rootStorage = RootStorage.OpenRead(fileName, StorageModeFlags.StrictValidation))
            {
                IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
                Assert.HasCount(subStorageCount, entries);

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

        byte[] data = TestData.CreateByteArray(1024);

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
            using (var rootStorage = RootStorage.OpenRead(fileName, StorageModeFlags.StrictValidation))
            {
                rootStorage.SwitchTo(memoryStream);

                IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
                Assert.HasCount(streamCount, entries);

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

        using (var rootStorage = RootStorage.Open(switchedMemoryStream, StorageModeFlags.StrictValidation))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.HasCount(subStorageCount, entries);

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{i}");
        }
    }

    [TestMethod]
    public void OpenReadOnlyTransactedStreamThrows()
    {
        string fileName = $"{nameof(OpenReadOnlyTransactedStreamThrows)}.cfs";
        using (FileStream createStream = File.Create(fileName))
        {
        }
        using FileStream stream = new(fileName, FileMode.Open, FileAccess.Read);
        Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(stream, StorageModeFlags.Transacted));
    }

    [TestMethod]
    public void OpenPathOverloadsDisposeFileStreamOnOpenFailure()
    {
        static void AssertThrowsAndCanReopen(string path, Action openAction)
        {
            Assert.Throws<Exception>(openAction);
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        string fileName = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(fileName, [0x01, 0x02, 0x03, 0x04]);

            AssertThrowsAndCanReopen(fileName, () => RootStorage.Open(fileName, FileMode.Open));
            AssertThrowsAndCanReopen(fileName, () => RootStorage.Open(fileName, FileMode.Open, FileAccess.ReadWrite));
            AssertThrowsAndCanReopen(fileName, () => RootStorage.OpenRead(fileName));
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    public void SwitchToFileDisposesFileStreamOnFailure()
    {
        string fileName = Path.GetTempFileName();

        try
        {
            using var rootStorage = RootStorage.CreateInMemory();
            rootStorage.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => rootStorage.SwitchTo(fileName));

            using FileStream stream = File.Open(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    public void DisposeWithoutChangesDoesNotUpdateLastWriteTime()
    {
        string fileName = Path.GetTempFileName();
        try
        {
            using (RootStorage rootStorage = RootStorage.Create(fileName))
            {
            }

            DateTime expectedLastWriteTimeUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(fileName, expectedLastWriteTimeUtc);

            using (RootStorage rootStorage = RootStorage.Open(fileName, FileMode.Open, FileAccess.ReadWrite))
            {
            }

            DateTime actualLastWriteTimeUtc = File.GetLastWriteTimeUtc(fileName);
            Assert.AreEqual(expectedLastWriteTimeUtc, actualLastWriteTimeUtc);
        }
        finally
        {
            TestFile.TryDelete(fileName);
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
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void TransactionSignatureNumberDoesNotIncrementOnFlush(Version version)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen);

        Assert.AreEqual(0u, rootStorage.Context.Header.TransactionSignatureNumber);

        rootStorage.Flush();
        Assert.AreEqual(0u, rootStorage.Context.Header.TransactionSignatureNumber);

        rootStorage.Flush();
        Assert.AreEqual(0u, rootStorage.Context.Header.TransactionSignatureNumber);

        memoryStream.Position = 0;
        using CfbBinaryReader reader = new(memoryStream);
        Header header = reader.ReadHeader();
        Assert.AreEqual(0u, header.TransactionSignatureNumber);
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void TransactionSignatureNumberIncrementsOnCommit(Version version)
    {
        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.Transacted | StorageModeFlags.LeaveOpen))
        {
            Assert.AreEqual(0u, rootStorage.Context.Header.TransactionSignatureNumber);

            rootStorage.Commit();
            Assert.AreEqual(1u, rootStorage.Context.Header.TransactionSignatureNumber);

            rootStorage.Commit();
            Assert.AreEqual(2u, rootStorage.Context.Header.TransactionSignatureNumber);
        }

        memoryStream.Position = 0;
        using var rootStorage2 = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen);
        Assert.AreEqual(2u, rootStorage2.Context.Header.TransactionSignatureNumber);
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
        using var root = RootStorage.OpenRead("DirectoryTreeCycle.cfb", StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => root.TryOpenStorage("AB", out _));
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnEnumerate()
    {
        using var root = RootStorage.OpenRead("DirectoryTreeCycle.cfb", StorageModeFlags.StrictValidation);
        IEnumerator<EntryInfo> enumerator = root.EnumerateEntries().GetEnumerator();
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
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => root.CreateStorage("AB"));
    }

    [TestMethod]
    public void DirectoryTreeCycleThrowsFileFormatExceptionOnDelete()
    {
        using MemoryStream stream = TestData.CreateMemoryStreamFromFile("DirectoryTreeCycle.cfb");
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => root.Delete("AB"));
    }
}
