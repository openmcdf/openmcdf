namespace OpenMcdf.Tests;

[TestClass]
public sealed class ArgumentValidationTests
{
    [TestMethod]
    public void Storage_NullName()
    {
        using var root = RootStorage.CreateInMemory();
        Assert.ThrowsExactly<ArgumentNullException>(() => root.ContainsEntry(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.TryGetEntryInfo(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.CreateStorage(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.CreateStream(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.OpenStorage(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.TryOpenStorage(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.OpenStream(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.TryOpenStream(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.Delete(null!));
    }

    [TestMethod]
    public void Storage_CopyToNull()
    {
        using var root = RootStorage.CreateInMemory();
        Assert.ThrowsExactly<ArgumentNullException>(() => root.CopyTo(null!));
    }

    [TestMethod]
    public void Storage_TryGetEntryInfo_NotFound()
    {
        using var root = RootStorage.CreateInMemory();
        Assert.IsFalse(root.TryGetEntryInfo("missing", out EntryInfo info));
        Assert.AreEqual(default, info);
        Assert.IsFalse(root.ContainsEntry("missing"));
    }

    [TestMethod]
    public void Storage_RootCreationTimeMustBeZero()
    {
        using var root = RootStorage.CreateInMemory();
        Assert.ThrowsExactly<ArgumentException>(() => root.CreationFileTime = 1);
        root.CreationFileTime = 0;
    }

    [TestMethod]
    public void Storage_ObsoleteClsid()
    {
        using var root = RootStorage.CreateInMemory();
        var guid = Guid.NewGuid();
#pragma warning disable CS0618 // Type or member is obsolete
        root.CLISD = guid;
        Assert.AreEqual(guid, root.CLISD);
#pragma warning restore CS0618 // Type or member is obsolete
        Assert.AreEqual(guid, root.CLSID);
    }

    [TestMethod]
    public void Storage_InvalidDirectoryEntryType()
    {
        using var root = RootStorage.CreateInMemory();
        using (CfbStream stream = root.CreateStream("stream"))
        {
        }

        DirectoryEntry entry = root.Context.DirectoryEntries.GetDirectoryEntry(1);
        Assert.AreEqual(StorageType.Stream, entry.Type);
        Assert.ThrowsExactly<ArgumentException>(() => new Storage(root.ContextSite, entry, root));
    }

    [TestMethod]
    public void RootStorage_Create_InvalidArguments()
    {
        string fileName = Path.GetTempFileName();
        try
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.Create((string)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.Create((Stream)null!));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Create(fileName, Version.Unknown));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Create(fileName, Version.V3, StorageModeFlags.LeaveOpen));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.CreateInMemory(Version.Unknown));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.CreateInMemory(Version.V3, StorageModeFlags.LeaveOpen));
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    public void RootStorage_Open_InvalidArguments()
    {
        string fileName = Path.GetTempFileName();
        try
        {
            using (RootStorage.Create(fileName))
            {
            }

            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.Open((string)null!, FileMode.Open));
            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.Open((string)null!, FileMode.Open, FileAccess.Read));
            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.Open((Stream)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => RootStorage.OpenRead(null!));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(fileName, FileMode.Append));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(fileName, FileMode.Open, FileAccess.Write));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.Open(fileName, FileMode.Open, StorageModeFlags.LeaveOpen));
            Assert.ThrowsExactly<ArgumentException>(() => RootStorage.OpenRead(fileName, StorageModeFlags.LeaveOpen));
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    public void RootStorage_Open_InvalidFileDisposesStream()
    {
        string fileName = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(fileName, new byte[1024]);
            Assert.ThrowsExactly<FileFormatException>(() => RootStorage.Open(fileName, FileMode.Open));
            Assert.ThrowsExactly<FileFormatException>(() => RootStorage.Open(fileName, FileMode.Open, FileAccess.Read));
            Assert.ThrowsExactly<FileFormatException>(() => RootStorage.OpenRead(fileName));
            File.Delete(fileName);
        }
        finally
        {
            TestFile.TryDelete(fileName);
        }
    }

    [TestMethod]
    public void RootStorage_SwitchTo_InvalidArguments()
    {
        using var root = RootStorage.CreateInMemory();
        Assert.ThrowsExactly<ArgumentNullException>(() => root.SwitchTo((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => root.SwitchTo((string)null!));
    }

    [TestMethod]
    public void RootStorage_CanWriteAndCanCommit_AfterDispose()
    {
        var root = RootStorage.CreateInMemory();
        Assert.IsTrue(root.CanWrite);
        root.Dispose();
        Assert.IsFalse(root.CanWrite);
        Assert.IsFalse(root.CanCommit);
    }

    [TestMethod]
    public void RootStorage_Consolidate_UnsupportedStreamType()
    {
        using MemoryStream memoryStream = new();
        using BufferedStream bufferedStream = new(memoryStream);
        using var root = RootStorage.Create(bufferedStream, Version.V3, StorageModeFlags.LeaveOpen);
        Assert.ThrowsExactly<NotSupportedException>(() => root.Flush(true));
    }

    [TestMethod]
    public void CfbStream_SetLength_Negative()
    {
        using var root = RootStorage.CreateInMemory();
        using CfbStream stream = root.CreateStream("stream");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stream.SetLength(-1));
    }
}
