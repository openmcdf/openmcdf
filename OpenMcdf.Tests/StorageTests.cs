namespace OpenMcdf.Tests;

[TestClass]
public sealed class StorageTests
{
    [TestMethod]
    [DataRow("MultipleStorage.cfs", 1)]
    [DataRow("MultipleStorage2.cfs", 1)]
    [DataRow("MultipleStorage3.cfs", 1)]
    [DataRow("MultipleStorage4.cfs", 1)]
    public void EnumerateEntries(string fileName, long storageCount)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        IEnumerable<EntryInfo> storageEntries = rootStorage.EnumerateEntries();
        Assert.AreEqual(storageCount, storageEntries.Count());
    }

    [TestMethod]
    [DataRow("MultipleStorage.cfs")]
    public void OpenStorage(string fileName)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        Assert.IsTrue(rootStorage.TryOpenStorage("MyStorage", out Storage? _));
        Assert.IsFalse(rootStorage.TryOpenStorage(string.Empty, out Storage? _));

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => rootStorage.OpenStorage(string.Empty));

        Assert.IsTrue(rootStorage.ContainsEntry("MyStorage"));
        Assert.IsFalse(rootStorage.ContainsEntry("NonExistentStorage"));

        bool found = rootStorage.TryGetEntryInfo("MyStorage", out EntryInfo entryInfo);
        Assert.IsTrue(found);
        Assert.AreEqual("MyStorage", entryInfo.Name);

        Storage storage = rootStorage.OpenStorage("MyStorage");
        Assert.AreEqual("MyStorage", storage.EntryInfo.Name);
    }

    [TestMethod]
    [DataRow("FatChainLoop_v3.cfs")]
    [Ignore("Test file has multiple validation errors")]
    public void FatChainLoop(string fileName)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        Assert.ThrowsExactly<FileFormatException>(() => rootStorage.OpenStorage("Anything"));
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
    public void CreateStorage(Version version, int subStorageCount)
    {
        using MemoryStream memoryStream = new();

        // Test adding right sibling
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            for (int i = 0; i < subStorageCount; i++)
                rootStorage.CreateStorage($"Test{i}");
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.HasCount(subStorageCount, entries);

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{i}");
        }

        // Test adding left sibling
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            for (int i = 0; i < subStorageCount; i++)
                rootStorage.CreateStorage($"Test{subStorageCount - i}");
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen))
        {
            IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
            Assert.HasCount(subStorageCount, entries);

            for (int i = 0; i < subStorageCount; i++)
                rootStorage.OpenStorage($"Test{subStorageCount - i}");
        }

#if WINDOWS
        using (var rootStorage = StructuredStorage.Storage.Open(memoryStream))
        {
            IEnumerable<string> entries = rootStorage.EnumerateEntries();
            Assert.HasCount(subStorageCount, entries);

            for (int i = 0; i < subStorageCount; i++)
            {
                using StructuredStorage.Storage storage = rootStorage.OpenStorage($"Test{subStorageCount - i}");
            }
        }
#endif
    }

    [TestMethod]
    public void CreateInvalidStorageName()
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream);
        Assert.ThrowsExactly<ArgumentException>(() => rootStorage.CreateStorage("!"));
        Assert.ThrowsExactly<ArgumentException>(() => rootStorage.CreateStorage("/"));
        Assert.ThrowsExactly<ArgumentException>(() => rootStorage.CreateStorage(":"));
        Assert.ThrowsExactly<ArgumentException>(() => rootStorage.CreateStorage("\\"));
    }

    [TestMethod]
    [DataRow(Version.V3, 0)]
    [DataRow(Version.V3, 1)]
    [DataRow(Version.V4, 0)]
    [DataRow(Version.V4, 1)]
    public void CreateStorageFile(Version version, int subStorageCount)
    {
        string fileName = Path.GetTempFileName();

        try
        {
            using (var rootStorage = RootStorage.Create(fileName, version))
            {
                for (int i = 0; i < subStorageCount; i++)
                    rootStorage.CreateStorage($"Test{i}");
            }

            using (var rootStorage = RootStorage.OpenRead(fileName))
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
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void CreateDuplicateStorageThrowsException(Version version)
    {
        using MemoryStream memoryStream = new();
        using var rootStorage = RootStorage.Create(memoryStream, version);
        rootStorage.CreateStorage("Test");
        Assert.ThrowsExactly<IOException>(() => rootStorage.CreateStorage("Test"));
    }

    private static MemoryStream CreateDeleteTest(Version version, int count)
    {
        MemoryStream stream = new();
        using var rootStorage = RootStorage.Create(stream, version, StorageModeFlags.LeaveOpen);

        int mid = (count + 1) / 2;
        rootStorage.CreateStorage($"{mid}");
        for (int i = 0; i < count - 1; i++)
        {
            int name;
            if (i % 2 == 0)
                name = mid - i / 2 - 1;
            else
                name = mid + i / 2 + 1;
            rootStorage.CreateStorage($"{name}");
        }

        return stream;
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStorageLeaves(Version version)
    {
        using MemoryStream memoryStream = CreateDeleteTest(version, 3);
        using var rootStorage = RootStorage.Open(memoryStream);

        rootStorage.Delete("NonExistentEntry");
        Assert.HasCount(3, rootStorage.EnumerateEntries());

        // Left
        rootStorage.Delete("1");
        Assert.HasCount(2, rootStorage.EnumerateEntries());

        // Right
        rootStorage.Delete("3");
        Assert.HasCount(1, rootStorage.EnumerateEntries());

        // Root
        rootStorage.Delete("2");
        Assert.IsEmpty(rootStorage.EnumerateEntries());
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStorageRoot(Version version)
    {
        using MemoryStream memoryStream = CreateDeleteTest(version, 3);
        using var rootStorage = RootStorage.Open(memoryStream);

        rootStorage.Delete("2");
        Assert.HasCount(2, rootStorage.EnumerateEntries());

        rootStorage.Delete("1");
        Assert.HasCount(1, rootStorage.EnumerateEntries());

        rootStorage.Delete("3");
        Assert.IsEmpty(rootStorage.EnumerateEntries());
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStorageBranch(Version version)
    {
        const int count = 15;

        for (int i = 0; i < count; i++)
        {
            using MemoryStream memoryStream = CreateDeleteTest(version, count);
            using var rootStorage = RootStorage.Open(memoryStream);

            for (int j = 0; j < count; j++)
            {
                int name = (i + j) % count + 1;
                rootStorage.Delete($"{name}");
                Assert.HasCount(count - 1 - j, rootStorage.EnumerateEntries());
            }
        }
    }

#if WINDOWS
    private static string CreateBalancedDeleteTest(int count)
    {
        string fileName = $"BalancedDelete_{count}.cfs";
        File.Delete(fileName);
        using var rootStorage = StructuredStorage.Storage.Create(fileName);
        for (int i = 0; i < count; i++)
        {
            using StructuredStorage.Storage storage = rootStorage.CreateStorage($"{i + 1}");
        }

        return fileName;
    }

    [TestMethod]
    public void DeleteBalancedStorageBranch()
    {
        const int count = 31;

        for (int i = 0; i < count; i++)
        {
            string fileName = CreateBalancedDeleteTest(count);
            using var rootStorage = RootStorage.Open(fileName, FileMode.Open);

            for (int j = 0; j < count; j++)
            {
                int name = (i + j) % count + 1;
                rootStorage.Delete($"{name}");
                Assert.HasCount(count - 1 - j, rootStorage.EnumerateEntries());
            }
        }
    }
#endif

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStorageRecursively(Version version)
    {
        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            Storage storage = rootStorage.CreateStorage("Storage");
            Assert.HasCount(1, rootStorage.EnumerateEntries());

            Storage subStorage = storage.CreateStorage("SubStorage");
            using CfbStream subStream = subStorage.CreateStream("SubStream");
            Assert.HasCount(1, storage.EnumerateEntries());
        }

        using (var rootStorage = RootStorage.Open(memoryStream, StorageModeFlags.LeaveOpen))
        {
            rootStorage.Delete("Storage");
            Assert.IsEmpty(rootStorage.EnumerateEntries());
            rootStorage.Validate();
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            Assert.IsEmpty(rootStorage.EnumerateEntries());
            rootStorage.Validate();
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStorageWithStreamChildReusesFreedSectors(Version version)
    {
        using MemoryStream memoryStream = new();
        byte[] data = TestData.CreateByteArray(16 * 1024);

        using var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen);
        Storage storage = rootStorage.CreateStorage("Storage");
        using (CfbStream subStream = storage.CreateStream("SubStream"))
        {
            subStream.Write(data, 0, data.Length);
        }

        rootStorage.Flush();
        long initialLength = rootStorage.BaseStream.Length;

        rootStorage.Delete("Storage");
        rootStorage.Flush();

        using (CfbStream replacement = rootStorage.CreateStream("Replacement"))
        {
            replacement.Write(data, 0, data.Length);
        }

        rootStorage.Flush();

        Assert.AreEqual(initialLength, rootStorage.BaseStream.Length);
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void DeleteStream(Version version)
    {
        using MemoryStream memoryStream = new();
        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            rootStorage.CreateStream("Test");
            Assert.HasCount(1, rootStorage.EnumerateEntries());
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            rootStorage.Delete("Test");
            Assert.IsEmpty(rootStorage.EnumerateEntries());
        }
    }

    [TestMethod]
    [DataRow(Version.V3)]
    [DataRow(Version.V4)]
    public void GetAndSetMetadata(Version version)
    {
        using MemoryStream memoryStream = new();

        var guid = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        using (var rootStorage = RootStorage.Create(memoryStream, version, StorageModeFlags.LeaveOpen))
        {
            Storage storage = rootStorage.CreateStorage("Storage");

            Assert.AreEqual(Guid.Empty, storage.CLSID);
            Assert.AreNotEqual(FileTime.UtcZero, storage.CreationTime);
            Assert.AreNotEqual(FileTime.UtcZero, storage.ModifiedTime);
            Assert.AreEqual(0U, storage.StateBits);

            storage.CLSID = guid;
            storage.CreationTime = now;
            storage.ModifiedTime = now;
            storage.StateBits = 1U;
        }

        using (var rootStorage = RootStorage.Open(memoryStream))
        {
            Storage storage = rootStorage.OpenStorage("Storage");

            Assert.AreEqual(guid, storage.CLSID);
            Assert.AreEqual(now, storage.CreationTime);
            Assert.AreEqual(now, storage.ModifiedTime);
            Assert.AreEqual(1U, storage.StateBits);
        }
    }
}
