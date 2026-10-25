using Microsoft.IO;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

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
    public void OpenWithStrictValidationThrowsWhenStreamLengthExceedsMaximum()
    {
        long length = RootContext.MaximumV3StreamLength + 1;
        using var stream = new LengthReportingMemoryStream(File.ReadAllBytes("TestStream_v3_0.cfs"), length);
        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
        {
            using var rootStorage = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        });
        Assert.AreEqual($"Stream length {length} exceeds the maximum length {RootContext.MaximumV3StreamLength}.", exception.Message);
    }

    [TestMethod]
    [DataRow(Version.V3, 0x28)] // Directory sector count
    [DataRow(Version.V3, 0x2C)] // FAT sector count
    [DataRow(Version.V3, 0x40)] // Mini FAT sector count
    [DataRow(Version.V3, 0x48)] // DIFAT sector count
    [DataRow(Version.V4, 0x28)]
    [DataRow(Version.V4, 0x2C)]
    [DataRow(Version.V4, 0x40)]
    [DataRow(Version.V4, 0x48)]
    public void OpenWithStrictValidationThrowsWhenHeaderSectorCountsExceedStreamLength(Version version, int countOffset)
    {
        using MemoryStream createStream = new();
        using (var rootStorage = RootStorage.Create(createStream, version, StorageModeFlags.LeaveOpen))
        {
        }

        // A trailing partial sector cannot hold any sectors
        int sectorSize = version is Version.V3 ? 512 : 4096;
        createStream.SetLength(createStream.Length + sectorSize - 1);
        byte[] buffer = createStream.ToArray();
        uint sectorCount = (uint)((buffer.Length - sectorSize) / sectorSize);

        uint otherSectorCount = 0;
        foreach (int offset in (int[])[0x28, 0x2C, 0x40, 0x48])
        {
            if (offset != countOffset)
                otherSectorCount += BitConverter.ToUInt32(buffer, offset);
        }

        BitConverter.GetBytes(sectorCount - otherSectorCount).CopyTo(buffer, countOffset);
        using (var stream = new MemoryStream(buffer, writable: false))
        {
            using var rootStorage = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        }

        BitConverter.GetBytes(sectorCount - otherSectorCount + 1).CopyTo(buffer, countOffset);
        using (var stream = new MemoryStream(buffer, writable: false))
        {
            FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            {
                using var rootStorage = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
            });
            Assert.AreEqual("The sector counts declared in the header exceed the maximum possible for the stream length.", exception.Message);
        }

        using (var stream = new MemoryStream(buffer, writable: false))
        {
            using var rootStorage = RootStorage.Open(stream);
        }
    }

    [TestMethod]
    public void OpenWithStrictValidationWithDifatSectors()
    {
        // Enough data to require more FAT sectors than fit in the header's DIFAT array
        const int FatEntriesPerSector = 512 / sizeof(uint);
        byte[] data = new byte[(Header.DifatArrayLength + 1) * FatEntriesPerSector * 512];

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, Version.V3, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("Test");
            stream.Write(data, 0, data.Length);
        }

        byte[] buffer = memoryStream.ToArray();
        Assert.AreEqual(1u, BitConverter.ToUInt32(buffer, 0x48));

        using var readStream = new MemoryStream(buffer, writable: false);
        using var readRootStorage = RootStorage.Open(readStream, StorageModeFlags.StrictValidation);
        Assert.IsTrue(readRootStorage.Validate());
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DifatChainLoopThrowsFileFormatException(Version version)
    {
        using MemoryStream createStream = new();
        using (var createRootStorage = RootStorage.Create(createStream, version, StorageModeFlags.LeaveOpen))
        {
        }

        int sectorSize = version is Version.V3 ? 512 : 4096;
        int fatEntriesPerSector = sectorSize / sizeof(uint);
        int difatEntriesPerSector = fatEntriesPerSector - 1;

        // Append a DIFAT sector that links to itself
        byte[] buffer = new byte[createStream.Length + sectorSize];
        createStream.ToArray().CopyTo(buffer, 0);
        uint difatSectorId = (uint)((createStream.Length - sectorSize) / sectorSize);
        BitConverter.GetBytes(difatSectorId).CopyTo(buffer, buffer.Length - sizeof(uint));

        BitConverter.GetBytes(uint.MaxValue).CopyTo(buffer, 0x2C); // FAT sector count
        BitConverter.GetBytes(difatSectorId).CopyTo(buffer, 0x44); // First DIFAT sector ID
        BitConverter.GetBytes(uint.MaxValue).CopyTo(buffer, 0x48); // DIFAT sector count

        using var stream = new MemoryStream(buffer, writable: false);
        using var rootStorage = RootStorage.Open(stream);
        Fat fat = rootStorage.Context.Fat;

        // Each DIFAT sector must be a distinct sector, so the chain can be no longer than the sector count
        uint maxDifatSectorCount = (uint)((buffer.Length - sectorSize) / sectorSize);
        uint GetFirstKeyForDifatIndex(uint difatIndex) => (uint)((Header.DifatArrayLength + (difatIndex * difatEntriesPerSector)) * fatEntriesPerSector);

        Assert.IsTrue(fat.TryGetValue(GetFirstKeyForDifatIndex(maxDifatSectorCount - 1), out _));
        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() => fat.TryGetValue(GetFirstKeyForDifatIndex(maxDifatSectorCount), out _));
        Assert.AreEqual("DIFAT chain index is greater than the maximum for the stream length.", exception.Message);
    }

    [TestMethod]
    public void OpenNonStrictWithNonZeroHeaderCLSID()
    {
        var expectedHeaderCLSID = Guid.Parse("00020906-0000-0000-c000-000000000046");
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
        var expectedHeaderCLSID = Guid.Parse("00020906-0000-0000-c000-000000000046");

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
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void ConsolidateWithOpenObjects(Version version)
    {
        byte[] miniData = TestData.CreateByteArray(1024);
        byte[] fatData = TestData.CreateByteArray(16 * 1024);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using (CfbStream stream = rootStorage.CreateStream("Deleted"))
                stream.Write(fatData, 0, fatData.Length);

            Storage storage = rootStorage.CreateStorage("Storage");
            using CfbStream miniStream = rootStorage.CreateStream("MiniStream");
            using CfbStream fatStream = rootStorage.CreateStream("FatStream");
            miniStream.Write(miniData, 0, miniData.Length);
            fatStream.Write(fatData, 0, fatData.Length);
            miniStream.Flush();
            fatStream.Flush();

            // Leave directory updates pending without changing the flushed data, so disposal would write stale entries
            miniStream.SetLength(miniStream.Length);
            fatStream.SetLength(fatStream.Length);

            // Consolidation moves the remaining entries and their data
            rootStorage.Delete("Deleted");

            // Enumeration is lazy, so it can start or continue after consolidating
            IEnumerable<EntryInfo> delayedEntries = storage.EnumerateEntries();
            IEnumerable<EntryInfo> delayedRootEntries = rootStorage.EnumerateEntries();
            using IEnumerator<EntryInfo> partialRootEntries = rootStorage.EnumerateEntries().GetEnumerator();
            Assert.IsTrue(partialRootEntries.MoveNext());

            rootStorage.Flush(true);

            Assert.ThrowsExactly<InvalidOperationException>(() => delayedEntries.ToList());
            Assert.ThrowsExactly<InvalidOperationException>(() => partialRootEntries.MoveNext());
            Assert.HasCount(3, delayedRootEntries);

            foreach (CfbStream stream in new[] { miniStream, fatStream })
            {
                byte[] buffer = new byte[16];
                Assert.IsFalse(stream.CanRead);
                Assert.IsFalse(stream.CanSeek);
                Assert.IsFalse(stream.CanWrite);
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Length);
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Position);
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Position = 0);
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.EntryInfo);
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Seek(0, SeekOrigin.Begin));
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Read(buffer, 0, buffer.Length));
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.Write(buffer, 0, buffer.Length));
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.ReadByte());
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.WriteByte(0));
                Assert.ThrowsExactly<InvalidOperationException>(() => stream.SetLength(0));
                Assert.ThrowsExactly<InvalidOperationException>(stream.Flush);
            }

            Assert.ThrowsExactly<InvalidOperationException>(() => storage.EntryInfo);
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.CLSID = Guid.NewGuid());
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.EnumerateEntries());
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.CreateStream("Child"));
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.CopyTo(rootStorage));
            Assert.ThrowsExactly<InvalidOperationException>(() => rootStorage.CopyTo(storage));

            // The root storage and objects opened after consolidating remain usable
            Assert.HasCount(3, rootStorage.EnumerateEntries());
            using (CfbStream stream = rootStorage.OpenStream("MiniStream"))
                CollectionAssert.AreEqual(miniData, ReadAll(stream));
            rootStorage.OpenStorage("Storage").CreateStorage("Child");

            // Disposing the invalidated streams must not write to the consolidated directory
            miniStream.Dispose();
            fatStream.Dispose();
            Assert.IsFalse(miniStream.CanRead);
            Assert.IsFalse(fatStream.CanRead);
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.StrictValidation))
        {
            Assert.IsTrue(rootStorage.Validate());
            Assert.IsFalse(rootStorage.ContainsEntry("Deleted"));
            Assert.IsTrue(rootStorage.OpenStorage("Storage").ContainsEntry("Child"));

            using (CfbStream stream = rootStorage.OpenStream("MiniStream"))
                CollectionAssert.AreEqual(miniData, ReadAll(stream));

            using (CfbStream stream = rootStorage.OpenStream("FatStream"))
                CollectionAssert.AreEqual(fatData, ReadAll(stream));
        }

        static byte[] ReadAll(Stream stream)
        {
            using MemoryStream memoryStream = new();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void ConsolidateReadOnly(Version version)
    {
        byte[] data = TestData.CreateByteArray(1024);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            Storage storage = rootStorage.CreateStorage("Storage");
            using (CfbStream stream = storage.CreateStream("Stream"))
                stream.Write(data, 0, data.Length);
            rootStorage.CreateStream("Sibling");
        }

        using MemoryStream readOnlyStream = new(memoryStream.ToArray(), false);
        using (var rootStorage = RootStorage.Open(readOnlyStream))
        {
            Storage storage = rootStorage.OpenStorage("Storage");
            using CfbStream stream = storage.OpenStream("Stream");
            using IEnumerator<EntryInfo> entries = rootStorage.EnumerateEntries().GetEnumerator();
            Assert.IsTrue(entries.MoveNext());

            Assert.ThrowsExactly<NotSupportedException>(() => rootStorage.Flush(true));

            // The failed consolidation must not invalidate open objects
            byte[] buffer = new byte[data.Length];
            Assert.AreEqual(data.Length, stream.Read(buffer, 0, buffer.Length));
            CollectionAssert.AreEqual(data, buffer);
            Assert.IsTrue(storage.ContainsEntry("Stream"));
            Assert.IsTrue(entries.MoveNext());
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void ConsolidatePreservesMetadata(Version version)
    {
        Guid headerClsid = Guid.NewGuid();
        Guid rootClsid = Guid.NewGuid();
        Guid storageClsid = Guid.NewGuid();
        Guid nestedClsid = Guid.NewGuid();

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            rootStorage.HeaderCLSID = headerClsid;
            rootStorage.CLSID = rootClsid;
            rootStorage.StateBits = 1;
            rootStorage.ModifiedFileTime = 2;

            Storage storage = rootStorage.CreateStorage("Storage");
            storage.CLSID = storageClsid;
            storage.StateBits = 3;
            storage.CreationFileTime = 4;
            storage.ModifiedFileTime = 5;

            Storage nested = storage.CreateStorage("Nested");
            nested.CLSID = nestedClsid;
            nested.StateBits = 6;

            rootStorage.CreateStream("Deleted");
            rootStorage.Delete("Deleted");
            rootStorage.Flush(true);
        }

        // Strict validation requires an empty header CLSID
        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            Assert.IsTrue(rootStorage.Validate());
            Assert.AreEqual(headerClsid, rootStorage.HeaderCLSID);
            Assert.AreEqual(rootClsid, rootStorage.CLSID);
            Assert.AreEqual(1u, rootStorage.StateBits);
            Assert.AreEqual(0ul, rootStorage.CreationFileTime);
            Assert.AreEqual(2ul, rootStorage.ModifiedFileTime);

            Storage storage = rootStorage.OpenStorage("Storage");
            Assert.AreEqual(storageClsid, storage.CLSID);
            Assert.AreEqual(3u, storage.StateBits);
            Assert.AreEqual(4ul, storage.CreationFileTime);
            Assert.AreEqual(5ul, storage.ModifiedFileTime);

            Storage nested = storage.OpenStorage("Nested");
            Assert.AreEqual(nestedClsid, nested.CLSID);
            Assert.AreEqual(6u, nested.StateBits);
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void ModifyAfterRevert(Version version)
    {
        byte[] data = TestData.CreateByteArray(5000);

        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = rootStorage.CreateStream("Committed");
            stream.Write(data, 0, data.Length);
        }

        long originalLength = memoryStream.Length;

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen | StorageModeFlags.Transacted))
        {
            // Grow the FAT, mini FAT and directory so the header and caches change
            for (int i = 0; i < 200; i++)
            {
                using CfbStream stream = rootStorage.CreateStream($"Reverted{i}");
                stream.Write(data, 0, data.Length);
            }

            Storage storage = rootStorage.CreateStorage("RevertedStorage");
            using CfbStream openStream = rootStorage.CreateStream("RevertedOpen");
            openStream.Write(data, 0, 100);

            rootStorage.Revert();

            // Objects opened before reverting may refer to entries that no longer exist
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.EntryInfo);
            Assert.ThrowsExactly<InvalidOperationException>(() => openStream.Write(data, 0, 1));
            Assert.IsFalse(openStream.CanWrite);

            Assert.AreEqual("Committed", Assert.ContainsSingle(rootStorage.EnumerateEntries()).Name);

            using (CfbStream stream = rootStorage.CreateStream("Added"))
                stream.Write(data, 0, 100);

            rootStorage.Commit();
        }

        // The reverted streams total 1 MB, while the added stream only needs a few more sectors
        Assert.IsLessThan(originalLength + (64 * 1024), memoryStream.Length);

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.StrictValidation))
        {
            Assert.IsTrue(rootStorage.Validate());
            CollectionAssert.AreEquivalent(new[] { "Committed", "Added" }, rootStorage.EnumerateEntries().Select(e => e.Name).ToList());

            using CfbStream stream = rootStorage.OpenStream("Committed");
            byte[] actual = new byte[data.Length];
            stream.ReadExactly(actual);
            CollectionAssert.AreEqual(data, actual);
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void RevertNewTransactedFile(Version version)
    {
        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen | StorageModeFlags.Transacted))
        {
            rootStorage.CreateStorage("Reverted");
            rootStorage.Revert();

            Assert.IsEmpty(rootStorage.EnumerateEntries());
            rootStorage.CreateStorage("Added");
            rootStorage.Commit();
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.StrictValidation))
        {
            Assert.IsTrue(rootStorage.Validate());
            Assert.AreEqual(version, (Version)rootStorage.Context.Header.MajorVersion);
            Assert.AreEqual("Added", Assert.ContainsSingle(rootStorage.EnumerateEntries()).Name);
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
    [DataRow(Version.V3, false)]
    [DataRow(Version.V3, true)]
    [DataRow(Version.V4, false)]
    [DataRow(Version.V4, true)]
    public void SwitchToWithOpenObjects(Version version, bool transacted)
    {
        byte[] miniData = TestData.CreateByteArray(1024);
        byte[] fatData = TestData.CreateByteArray(16 * 1024);
        StorageModeFlags flags = StorageModeFlags.LeaveOpen | (transacted ? StorageModeFlags.Transacted : StorageModeFlags.None);

        using MemoryStream originalMemoryStream = new();
        using MemoryStream switchedMemoryStream = new();
        var clsid = Guid.NewGuid();
        using (var rootStorage = RootStorage.Create(originalMemoryStream, version, flags))
        {
            // Create all siblings before opening the objects held across the switch,
            // so their directory entries are not stale for reasons unrelated to switching
            Storage streams = rootStorage.CreateStorage("Streams");
            using (CfbStream stream = streams.CreateStream("MiniStream"))
                stream.Write(miniData, 0, miniData.Length);
            using (CfbStream stream = streams.CreateStream("FatStream"))
                stream.Write(fatData, 0, fatData.Length);
            rootStorage.CreateStorage("Storage");

            streams = rootStorage.OpenStorage("Streams");
            Storage storage = rootStorage.OpenStorage("Storage");
            using CfbStream miniStream = streams.OpenStream("MiniStream");
            using CfbStream fatStream = streams.OpenStream("FatStream");
            miniStream.Seek(0, SeekOrigin.End);
            fatStream.Seek(0, SeekOrigin.End);

            rootStorage.SwitchTo(switchedMemoryStream);

            // Extend the FAT chain, mini FAT chain, mini stream and directory through objects opened before the switch
            miniStream.Write(miniData, 0, miniData.Length);
            fatStream.Write(fatData, 0, fatData.Length);
            storage.CreateStorage("Child");
            using (CfbStream newMiniStream = rootStorage.CreateStream("NewMiniStream"))
                newMiniStream.Write(miniData, 0, miniData.Length);

            // Writes the root entry, which must reflect the mini stream growth since the switch
            rootStorage.CLSID = clsid;

            miniStream.Flush();
            fatStream.Flush();
            if (transacted)
                rootStorage.Commit();
        }

        using (var rootStorage = RootStorage.Open(switchedMemoryStream, StorageModeFlags.StrictValidation))
        {
            Assert.IsTrue(rootStorage.Validate());

            Assert.AreEqual(clsid, rootStorage.CLSID);
            Assert.IsTrue(rootStorage.OpenStorage("Storage").ContainsEntry("Child"));

            Storage streams = rootStorage.OpenStorage("Streams");
            using (CfbStream miniStream = streams.OpenStream("MiniStream"))
                CollectionAssert.AreEqual(miniData.Concat(miniData).ToArray(), ReadAll(miniStream));

            using (CfbStream fatStream = streams.OpenStream("FatStream"))
                CollectionAssert.AreEqual(fatData.Concat(fatData).ToArray(), ReadAll(fatStream));

            using (CfbStream newMiniStream = rootStorage.OpenStream("NewMiniStream"))
                CollectionAssert.AreEqual(miniData, ReadAll(newMiniStream));
        }

        static byte[] ReadAll(Stream stream)
        {
            using MemoryStream memoryStream = new();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
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
            using (var rootStorage = RootStorage.Create(fileName))
            {
            }

            DateTime expectedLastWriteTimeUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(fileName, expectedLastWriteTimeUtc);

            using (var rootStorage = RootStorage.Open(fileName, FileMode.Open, FileAccess.ReadWrite))
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

    static MemoryStream CreateStorageCycle(Version version, bool linkToSelf)
    {
        MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, version, StorageModeFlags.LeaveOpen))
        {
            Storage a = rootStorage.CreateStorage("A");
            a.CreateStorage("B");

            // Link the children of "B" (or "A") back to "A"
            DirectoryEntries directories = rootStorage.Context.DirectoryEntries;
            DirectoryEntry entryA = rootStorage.EnumerateDirectoryEntries().Single();
            DirectoryEntry entry = linkToSelf ? entryA : a.EnumerateDirectoryEntries().Single();
            entry.ChildId = entryA.Id;
            directories.Write(entry);
        }

        stream.Position = 0;
        return stream;
    }

    [TestMethod]
    [DataRow(Version.V3, false)]
    [DataRow(Version.V3, true)]
    [DataRow(Version.V4, false)]
    [DataRow(Version.V4, true)]
    public void StorageCycleThrowsFileFormatExceptionOnCopyTo(Version version, bool linkToSelf)
    {
        using MemoryStream stream = CreateStorageCycle(version, linkToSelf);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        using var destination = RootStorage.CreateInMemory(version);
        Assert.ThrowsExactly<FileFormatException>(() => root.CopyTo(destination));
    }

    [TestMethod]
    [DataRow(Version.V3, false)]
    [DataRow(Version.V3, true)]
    [DataRow(Version.V4, false)]
    [DataRow(Version.V4, true)]
    public void StorageCycleThrowsFileFormatExceptionOnDelete(Version version, bool linkToSelf)
    {
        using MemoryStream stream = CreateStorageCycle(version, linkToSelf);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => root.Delete("A"));
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void StorageCycleThrowsFileFormatExceptionOnOpen(Version version)
    {
        using MemoryStream stream = CreateStorageCycle(version, false);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Storage b = root.OpenStorage("A").OpenStorage("B");
        Assert.ThrowsExactly<FileFormatException>(() => b.OpenStorage("A"));
    }

    const int StorageChainDepth = 1000;

    static string StorageChainName(int value) => $"S{value:D4}";

    // Nests storages S0000/S0001/..., optionally linking the children of the last back to the first
    static MemoryStream CreateStorageChain(Version version, bool cyclic)
    {
        MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, version, StorageModeFlags.LeaveOpen))
        {
            // Create the storages in random order, so adding them is fast, then link them into a chain
            Random random = new(1);
            foreach (int i in Enumerable.Range(0, StorageChainDepth).OrderBy(_ => random.Next()))
                rootStorage.CreateStorage(StorageChainName(i));

            DirectoryEntries directories = rootStorage.Context.DirectoryEntries;
            var entries = rootStorage.EnumerateDirectoryEntries().ToDictionary(e => e.NameString);
            for (int i = 0; i < StorageChainDepth; i++)
            {
                DirectoryEntry entry = entries[StorageChainName(i)];
                entry.LeftSiblingId = StreamId.NoStream;
                entry.RightSiblingId = StreamId.NoStream;
                if (i + 1 < StorageChainDepth)
                    entry.ChildId = entries[StorageChainName(i + 1)].Id;
                else
                    entry.ChildId = cyclic ? entries[StorageChainName(0)].Id : StreamId.NoStream;
                directories.Write(entry);
            }

            DirectoryEntry root = directories.GetRootEntry();
            root.ChildId = entries[StorageChainName(0)].Id;
            directories.Write(root);
        }

        stream.Position = 0;
        return stream;
    }

    // Runs the action on a small stack, which recursing once per nesting level of the storage chain would overflow
    static void RunWithSmallStack(Action action)
    {
        Exception? exception = null;
        Thread thread = new(Run, 256 * 1024);
        thread.Start();
        thread.Join();

        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();

        void Run()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void CopyToDeeplyNestedStorages(Version version)
    {
        using MemoryStream stream = CreateStorageChain(version, false);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        using var destination = RootStorage.CreateInMemory(version);
        RunWithSmallStack(() => root.CopyTo(destination));

        Assert.IsTrue(destination.Validate());
        Storage storage = destination;
        for (int i = 0; i < StorageChainDepth; i++)
            storage = storage.OpenStorage(StorageChainName(i));
        Assert.IsEmpty(storage.EnumerateEntries());
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteDeeplyNestedStorages(Version version)
    {
        using MemoryStream stream = CreateStorageChain(version, false);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        RunWithSmallStack(() => root.Delete(StorageChainName(0)));

        Assert.IsTrue(root.Validate());
        Assert.IsEmpty(root.EnumerateEntries());
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void LongStorageCycleThrowsFileFormatExceptionOnCopyTo(Version version)
    {
        using MemoryStream stream = CreateStorageChain(version, true);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        using var destination = RootStorage.CreateInMemory(version);
        Assert.ThrowsExactly<FileFormatException>(() => RunWithSmallStack(() => root.CopyTo(destination)));
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void LongStorageCycleThrowsFileFormatExceptionOnDelete(Version version)
    {
        using MemoryStream stream = CreateStorageChain(version, true);
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Assert.ThrowsExactly<FileFormatException>(() => RunWithSmallStack(() => root.Delete(StorageChainName(0))));
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void SharedStorageThrowsFileFormatExceptionOnCopyTo(Version version)
    {
        using MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, version, StorageModeFlags.LeaveOpen))
        {
            Storage a = rootStorage.CreateStorage("A");
            Storage b = rootStorage.CreateStorage("B");
            a.CreateStorage("C");

            // Link the children of "B" to "C", so it is also a child of "A"
            DirectoryEntries directories = rootStorage.Context.DirectoryEntries;
            DirectoryEntry entryB = rootStorage.EnumerateDirectoryEntries().Single(e => e.NameString == "B");
            entryB.ChildId = a.EnumerateDirectoryEntries().Single().Id;
            directories.Write(entryB);
        }

        stream.Position = 0;
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        using var destination = RootStorage.CreateInMemory(version);
        Assert.ThrowsExactly<FileFormatException>(() => root.CopyTo(destination));
    }

    [TestMethod]
    [DataRow(Version.V3, false)]
    [DataRow(Version.V3, true)]
    [DataRow(Version.V4, false)]
    [DataRow(Version.V4, true)]
    public void CopyToSharedDestinationThrowsFileFormatException(Version version, bool intoChild)
    {
        using MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, version, StorageModeFlags.LeaveOpen))
        {
            rootStorage.CreateStorage("A");
            rootStorage.CreateStorage("B").CreateStorage("C");

            // Link the children of "A" to "B", so it is also a child of the root
            DirectoryEntries directories = rootStorage.Context.DirectoryEntries;
            var entries = rootStorage.EnumerateDirectoryEntries().ToDictionary(e => e.NameString);
            DirectoryEntry entryA = entries["A"];
            entryA.ChildId = entries["B"].Id;
            directories.Write(entryA);
        }

        stream.Position = 0;
        using var root = RootStorage.Open(stream, StorageModeFlags.StrictValidation);
        Storage a = root.OpenStorage("A");

        // Opened through the root, so the parents of the destination do not include "A"
        Storage destination = root.OpenStorage("B");
        if (intoChild)
            destination = destination.OpenStorage("C");

        Assert.ThrowsExactly<FileFormatException>(() => a.CopyTo(destination));
    }

    [TestMethod]
    public void CopyToSelfOrDescendantThrowsArgumentException()
    {
        using var root = RootStorage.CreateInMemory();
        Storage a = root.CreateStorage("A");
        Storage b = a.CreateStorage("B");

        Assert.ThrowsExactly<ArgumentException>(() => a.CopyTo(a));
        Assert.ThrowsExactly<ArgumentException>(() => a.CopyTo(b));
        Assert.ThrowsExactly<ArgumentException>(() => root.CopyTo(b));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(10)]
    [DataRow(40)]
    public void DirectoryTreeSharedSiblingThrowsFileFormatExceptionOnEnumerate(int depth)
    {
        // Each level has a parent P whose left sibling L and right sibling R both link to the next level's P,
        // so without detection each nested level doubles the number of entries visited.
        // Names must satisfy L[0] < L[1] < ... < P[n] < R[n] < ... < P[0] < R[0] to pass the local order checks.
        static string Name(int value) => $"E{value:D4}";

        using MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, Version.V3, StorageModeFlags.LeaveOpen))
        {
            for (int i = 0; i < 3 * depth; i++)
                rootStorage.CreateStream(Name(i)).Dispose();

            DirectoryEntries directories = rootStorage.Context.DirectoryEntries;
            var entries = rootStorage.EnumerateDirectoryEntries().ToDictionary(e => e.NameString);

            DirectoryEntry? next = null;
            for (int level = depth - 1; level >= 0; level--)
            {
                int p = depth + (2 * (depth - 1 - level));
                DirectoryEntry left = entries[Name(level)];
                DirectoryEntry parent = entries[Name(p)];
                DirectoryEntry right = entries[Name(p + 1)];

                parent.LeftSiblingId = left.Id;
                parent.RightSiblingId = right.Id;
                left.LeftSiblingId = StreamId.NoStream;
                left.RightSiblingId = next?.Id ?? StreamId.NoStream;
                right.LeftSiblingId = next?.Id ?? StreamId.NoStream;
                right.RightSiblingId = StreamId.NoStream;

                foreach (DirectoryEntry entry in new[] { left, parent, right })
                {
                    entry.Color = NodeColor.Black;
                    directories.Write(entry);
                }

                next = parent;
            }

            DirectoryEntry root = directories.GetRootEntry();
            root.ChildId = next!.Id;
            directories.Write(root);
        }

        stream.Position = 0;
        using var reopened = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        Assert.ThrowsExactly<FileFormatException>(() => reopened.EnumerateEntries().ToList());
    }

    sealed class LengthReportingMemoryStream : MemoryStream
    {
        readonly long length;

        public LengthReportingMemoryStream(byte[] buffer, long length)
            : base(buffer, writable: false)
            => this.length = length;

        public override long Length => length;
    }
}
