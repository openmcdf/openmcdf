namespace OpenMcdf.Tests;

[TestClass]
public sealed class TransactedStreamTests
{
    const int SectorSize = 512;

    static TransactedStream CreateStream(RootStorage root, byte[] original)
    {
        MemoryStream originalStream = new(original.Length + SectorSize * 4);
        originalStream.Write(original, 0, original.Length);
        originalStream.Position = 0;
        return new TransactedStream(root.ContextSite, originalStream, new MemoryStream());
    }

    [TestMethod]
    public void PositionAndSetLength()
    {
        using var root = RootStorage.CreateInMemory();
        using TransactedStream stream = CreateStream(root, new byte[SectorSize * 2]);
        stream.Position = 100;
        Assert.AreEqual(100, stream.Position);
        Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(0));
    }

    [TestMethod]
    public void WriteByteAndReadByte()
    {
        using var root = RootStorage.CreateInMemory();
        using TransactedStream stream = CreateStream(root, new byte[SectorSize * 2]);
        stream.Position = 10;
        stream.WriteByte(0x42);
        stream.Position = 10;
        Assert.AreEqual(0x42, stream.ReadByte());
    }

    [TestMethod]
    public void WritePartialSectorCopiesExistingData()
    {
        using var root = RootStorage.CreateInMemory();
        byte[] original = TestData.CreateByteArray(SectorSize * 2);
        using TransactedStream stream = CreateStream(root, original);

        // Partial write within an existing sector keeps the rest of the sector
        stream.Position = 100;
        byte[] buffer = [1, 2, 3, 4];
        stream.Write(buffer, 0, buffer.Length);

        stream.Position = 98;
        byte[] actual = new byte[8];
        Assert.AreEqual(actual.Length, stream.Read(actual, 0, actual.Length));
        CollectionAssert.AreEqual(new byte[] { 98, 99, 1, 2, 3, 4, 104, 105 }, actual);
    }

    [TestMethod]
    public void WritePartialSectorBeyondOriginalLength()
    {
        using var root = RootStorage.CreateInMemory();
        using TransactedStream stream = CreateStream(root, new byte[SectorSize]);

        // Nothing to copy since the original stream ends before the sector
        stream.Position = SectorSize * 3 + 10;
        byte[] buffer = [1, 2, 3, 4];
        stream.Write(buffer, 0, buffer.Length);
        Assert.AreEqual(SectorSize, stream.Length); // Length is that of the overlay, which only holds the dirty sector

        stream.Position = SectorSize * 3 + 10;
        byte[] actual = new byte[4];
        Assert.AreEqual(actual.Length, stream.Read(actual, 0, actual.Length));
        CollectionAssert.AreEqual(buffer, actual);

#if !NETFRAMEWORK
        // The span overload
        stream.Position = SectorSize * 5 + 10;
        stream.Write(buffer.AsSpan());
        stream.Position = SectorSize * 5 + 10;
        Assert.AreEqual(actual.Length, stream.Read(actual.AsSpan()));
        CollectionAssert.AreEqual(buffer, actual);
#endif
    }

    [TestMethod]
    public void WriteWholeSectorAcrossSectors()
    {
        using var root = RootStorage.CreateInMemory();
        byte[] original = TestData.CreateByteArray(SectorSize * 2);
        using TransactedStream stream = CreateStream(root, original);
        byte[] buffer = new byte[SectorSize + 20];
        buffer.AsSpan().Fill((byte)0xAA);
        stream.Write(buffer, 0, buffer.Length);
        stream.Position = 0;
        byte[] actual = new byte[buffer.Length];
        Assert.AreEqual(actual.Length, stream.Read(actual, 0, actual.Length));
        CollectionAssert.AreEqual(buffer, actual);
    }

    [TestMethod]
    public void CommitAndRevert()
    {
        using var root = RootStorage.CreateInMemory();
        using TransactedStream stream = CreateStream(root, new byte[SectorSize * 2]);
        stream.Position = 0;
        stream.Write([1, 2, 3], 0, 3);
        stream.Revert();
        stream.Position = 0;
        Assert.AreEqual(0, stream.ReadByte());

        stream.Position = 0;
        stream.Write([1, 2, 3], 0, 3);
        stream.Commit();
        stream.Flush();
        stream.Position = 0;
        Assert.AreEqual(1, stream.ReadByte());
    }
}
