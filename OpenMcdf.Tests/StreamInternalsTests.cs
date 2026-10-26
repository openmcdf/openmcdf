namespace OpenMcdf.Tests;

/// <summary>
/// Exercises <see cref="FatStream"/>, <see cref="MiniFatStream"/> and the directory entries directly,
/// for paths that <see cref="CfbStream"/> never reaches because it validates lengths first.
/// </summary>
[TestClass]
public sealed class StreamInternalsTests
{
    const int BigLength = 5000;
    const int SmallLength = 100;
    const int OtherLength = 300;

    static RootStorage CreateRoot(out DirectoryEntry big, out DirectoryEntry small, out DirectoryEntry empty)
    {
        var root = RootStorage.CreateInMemory();
        using (CfbStream stream = root.CreateStream("big"))
            stream.Write(TestData.CreateByteArray(BigLength), 0, BigLength);
        using (CfbStream stream = root.CreateStream("small"))
            stream.Write(TestData.CreateByteArray(SmallLength), 0, SmallLength);
        using (CfbStream stream = root.CreateStream("other"))
            stream.Write(TestData.CreateByteArray(OtherLength), 0, OtherLength);
        using (root.CreateStream("empty"))
        {
        }

        big = FindEntry(root, "big");
        small = FindEntry(root, "small");
        empty = FindEntry(root, "empty");
        Assert.AreEqual(BigLength, big.StreamLength);
        Assert.AreEqual(SmallLength, small.StreamLength);
        return root;
    }

    static DirectoryEntry FindEntry(RootStorage root, string name)
    {
        for (uint id = 1; id < 16; id++)
        {
            DirectoryEntry entry = root.Context.DirectoryEntries.GetDirectoryEntry(id);
            if (entry.NameString == name)
                return entry;
        }

        throw new AssertFailedException($"Entry {name} not found.");
    }

    [TestMethod]
    public void FatStream_ZeroCount()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        using FatStream stream = new(root.ContextSite, big);
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, 0));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(Span<byte>.Empty));
#endif
        stream.Write(buffer, 0, 0);
#if !NETFRAMEWORK
        stream.Write(ReadOnlySpan<byte>.Empty);
#endif
        Assert.AreEqual(0, stream.Position);
    }

    [TestMethod]
    public void FatStream_ReadAtEnd()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        using FatStream stream = new(root.ContextSite, big);
        stream.Position = stream.Length;
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(buffer.AsSpan()));
#endif
        Assert.AreEqual(-1, stream.ReadByte());
    }

    [TestMethod]
    public void FatStream_ReadAndWriteByte()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        using FatStream stream = new(root.ContextSite, big);
        Assert.IsTrue(stream.CanWrite);
        Assert.AreEqual(0, stream.ReadByte());
        Assert.AreEqual(1, stream.ReadByte());
        stream.Position = 0;
        stream.WriteByte(0x55);
        stream.Position = 0;
        Assert.AreEqual(0x55, stream.ReadByte());
    }

    [TestMethod]
    public void FatStream_ChainShorterThanLength()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        byte[] buffer = new byte[100000];

        // Reading from a position beyond the end of the chain
        using (FatStream stream = new(root.ContextSite, big.Id, big.StartSectorId, 100000))
        {
            stream.Position = 90000;
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer, 0, 10));
#if !NETFRAMEWORK
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer.AsSpan(0, 10)));
#endif
        }

        // Reading until the end of the chain
        using (FatStream stream = new(root.ContextSite, big.Id, big.StartSectorId, 100000))
        {
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer, 0, buffer.Length));
            stream.Position = 0;
#if !NETFRAMEWORK
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer.AsSpan()));
#endif
        }
    }

    [TestMethod]
    public void FatStream_SectorBeyondEndOfFile()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        using FatStream stream = new(root.ContextSite, big.Id, 100000, 100);
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(buffer.AsSpan()));
#endif
    }

    [TestMethod]
    public void FatStream_WriteBeyondLengthExtendsLength()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        byte[] buffer = TestData.CreateByteArray(100);

        using (FatStream stream = new(root.ContextSite, big.Id, big.StartSectorId, 10))
        {
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(100, stream.Length);
        }

#if !NETFRAMEWORK
        using (FatStream stream = new(root.ContextSite, big.Id, big.StartSectorId, 10))
        {
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(100, stream.Length);
        }
#endif
    }

    [TestMethod]
    [DataRow(StreamId.NoStream, false)]
    [DataRow(SectorType.EndOfChain, false)]
#if !NETFRAMEWORK
    [DataRow(StreamId.NoStream, true)]
    [DataRow(SectorType.EndOfChain, true)]
#endif
    public void FatStream_WriteToEmptyChain(uint startSectorId, bool span)
    {
        using RootStorage root = CreateRoot(out _, out _, out DirectoryEntry empty);
        byte[] buffer = TestData.CreateByteArray(BigLength);

        // Start beyond the first sector, so the chain is extended more than once before writing
        using (FatStream stream = new(root.ContextSite, empty.Id, startSectorId, 0))
        {
            stream.Position = 1000;
            if (!span)
                stream.Write(buffer, 1000, buffer.Length - 1000);
#if !NETFRAMEWORK
            else
                stream.Write(buffer.AsSpan(1000));
#endif
        }

        DirectoryEntry entry = FindEntry(root, "empty");
        Assert.AreEqual(BigLength, entry.StreamLength);
        using CfbStream cfbStream = root.OpenStream("empty");
        byte[] actual = new byte[BigLength];
        cfbStream.ReadExactly(actual);
        CollectionAssert.AreEqual(buffer.Skip(1000).ToArray(), actual.Skip(1000).ToArray());
        Assert.IsTrue(root.Validate());
    }

    [TestMethod]
    public void FatStream_CanWriteIsFalseAfterDispose()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        FatStream stream = new(root.ContextSite, big);
        Assert.IsTrue(stream.CanWrite);
        stream.Dispose();
        Assert.IsFalse(stream.CanWrite);
    }

    [TestMethod]
    public void FatStream_SwitchToMiniFatStreamFailureRestoresPosition()
    {
        using RootStorage root = CreateRoot(out DirectoryEntry big, out _, out _);
        using FatStream stream = new(root.ContextSite, big);
        stream.Position = 10;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stream.SwitchToMiniFatStream(BigLength));
        Assert.AreEqual(10, stream.Position);
    }

    [TestMethod]
    public void MiniFatStream_ZeroCount()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        using MiniFatStream stream = new(root.ContextSite, small);
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, 0));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(Span<byte>.Empty));
#endif
        stream.Write(buffer, 0, 0);
#if !NETFRAMEWORK
        stream.Write(ReadOnlySpan<byte>.Empty);
#endif
        Assert.AreEqual(0, stream.Position);
    }

    [TestMethod]
    public void MiniFatStream_ReadAtEnd()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        using MiniFatStream stream = new(root.ContextSite, small);
        stream.Position = stream.Length;
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(buffer.AsSpan()));
#endif
        Assert.AreEqual(-1, stream.ReadByte());
    }

    [TestMethod]
    public void MiniFatStream_ReadAndWriteByte()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        using MiniFatStream stream = new(root.ContextSite, small);
        Assert.IsTrue(stream.CanWrite);
        Assert.AreEqual(0, stream.ReadByte());
        stream.Position = 0;
        stream.WriteByte(0x55);
        stream.Position = 0;
        Assert.AreEqual(0x55, stream.ReadByte());
    }

    [TestMethod]
    public void MiniFatStream_ChainShorterThanLength()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        byte[] buffer = new byte[100000];

        // Reading from a position beyond the end of the chain
        using (MiniFatStream stream = new(root.ContextSite, small.Id, small.StartSectorId, 100000))
        {
            stream.Position = 90000;
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer, 0, 10));
#if !NETFRAMEWORK
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer.AsSpan(0, 10)));
#endif
        }

        // Reading until the end of the chain, which is followed in the mini stream by the sectors of another stream
        using (MiniFatStream stream = new(root.ContextSite, small.Id, small.StartSectorId, OtherLength))
        {
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer, 0, buffer.Length));
            stream.Position = 0;
#if !NETFRAMEWORK
            Assert.ThrowsExactly<FileFormatException>(() => stream.Read(buffer.AsSpan()));
#endif
        }
    }

    [TestMethod]
    public void MiniFatStream_SectorBeyondEndOfMiniStream()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        using MiniFatStream stream = new(root.ContextSite, small.Id, 1000, 100);
        byte[] buffer = new byte[10];
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
#if !NETFRAMEWORK
        Assert.AreEqual(0, stream.Read(buffer.AsSpan()));
#endif
    }

    [TestMethod]
    public void MiniFatStream_SetLengthTooLong()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        using MiniFatStream stream = new(root.ContextSite, small);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stream.SetLength(Header.MiniStreamCutoffSize));
    }

    [TestMethod]
    public void MiniFatStream_WriteExtendsChain()
    {
        using RootStorage root = CreateRoot(out _, out _, out DirectoryEntry empty);
        byte[] buffer = TestData.CreateByteArray(SmallLength);

        using (MiniFatStream stream = new(root.ContextSite, empty.Id, StreamId.NoStream, 0))
        {
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(SmallLength, stream.Length);
            stream.Position = 0;
            byte[] actual = new byte[SmallLength];
            Assert.AreEqual(SmallLength, stream.Read(actual, 0, actual.Length));
            CollectionAssert.AreEqual(buffer, actual);
        }

#if !NETFRAMEWORK
        using (MiniFatStream stream = new(root.ContextSite, empty.Id, StreamId.NoStream, 0))
        {
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(SmallLength, stream.Length);
        }
#endif
    }

    [TestMethod]
    public void MiniFatStream_WriteBeyondLengthExtendsLength()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        byte[] buffer = TestData.CreateByteArray(30);

        using (MiniFatStream stream = new(root.ContextSite, small.Id, small.StartSectorId, 10))
        {
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(30, stream.Length);
        }

#if !NETFRAMEWORK
        using (MiniFatStream stream = new(root.ContextSite, small.Id, small.StartSectorId, 10))
        {
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(30, stream.Length);
        }
#endif
    }

    [TestMethod]
    public void MiniFatStream_WriteChainShorterThanLength()
    {
        using RootStorage root = CreateRoot(out _, out _, out DirectoryEntry empty);
        byte[] buffer = new byte[10];
        using MiniFatStream stream = new(root.ContextSite, empty.Id, SectorType.EndOfChain, 100);
        Assert.ThrowsExactly<FileFormatException>(() => stream.Write(buffer, 0, buffer.Length));
#if !NETFRAMEWORK
        Assert.ThrowsExactly<FileFormatException>(() => stream.Write(buffer.AsSpan()));
#endif
    }

    [TestMethod]
    public void MiniFatStream_CanWriteIsFalseAfterDispose()
    {
        using RootStorage root = CreateRoot(out _, out DirectoryEntry small, out _);
        MiniFatStream stream = new(root.ContextSite, small);
        Assert.IsTrue(stream.CanWrite);
        stream.Dispose();
        Assert.IsFalse(stream.CanWrite);
    }

    [TestMethod]
    public void MiniFatStream_SwitchToFatStreamFailureRestoresPosition()
    {
        using RootStorage root = CreateRoot(out _, out _, out DirectoryEntry empty);
        using MiniFatStream stream = new(root.ContextSite, empty.Id, SectorType.EndOfChain, 100);
        stream.Position = 10;
        Assert.ThrowsExactly<FileFormatException>(() => stream.SwitchToFatStream(100));
        Assert.AreEqual(10, stream.Position);
    }

    [TestMethod]
    public void DirectoryEntries_NotFound()
    {
        using RootStorage root = CreateRoot(out _, out _, out _);
        DirectoryEntries entries = root.Context.DirectoryEntries;
        Assert.ThrowsExactly<FileFormatException>(() => entries.GetDirectoryEntry(1000));
        Assert.ThrowsExactly<FileFormatException>(() => entries.TryGetDirectoryEntry(1000, true, out _));
        Assert.IsFalse(entries.TryGetDirectoryEntry(1000, false, out DirectoryEntry? entry));
        Assert.IsNull(entry);
        DirectoryEntry missing = new() { Id = 1000 };
        Assert.ThrowsExactly<FileFormatException>(() => entries.Write(missing));
    }
}
