using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace OpenMcdf.Tests;

/// <summary>
/// Exercises the internal FAT, mini FAT, DIFAT and directory enumerators directly, including their handling of corrupt data.
/// </summary>
[TestClass]
public sealed class EnumeratorTests
{
    // Large enough that the FAT needs more sectors than the 109 held by the header, which requires a DIFAT sector.
    const int DifatStreamLength = 8_000_000;

    static RootStorage CreateRoot(int streamLength = 5000)
    {
        var root = RootStorage.CreateInMemory();
        using (CfbStream stream = root.CreateStream("stream"))
            stream.Write(TestData.CreateByteArray(streamLength), 0, streamLength);
        return root;
    }

    static uint GetStartSectorId(RootStorage root)
    {
        for (uint id = 1; id < 16; id++)
        {
            DirectoryEntry entry = root.Context.DirectoryEntries.GetDirectoryEntry(id);
            if (entry.NameString == "stream")
                return entry.StartSectorId;
        }

        throw new AssertFailedException("Stream not found.");
    }

    static List<uint> GetChain(RootStorage root, uint startSectorId)
    {
        List<uint> chain = [];
        using FatChainEnumerator enumerator = new(root.ContextSite, startSectorId);
        while (enumerator.MoveNext())
            chain.Add(enumerator.Current);
        return chain;
    }

    static byte[] CreateFileBytes(int streamLength)
    {
        using MemoryStream stream = new();
        using (RootStorage root = RootStorage.Create(stream, Version.V3, StorageModeFlags.LeaveOpen))
        using (CfbStream cfbStream = root.CreateStream("stream"))
            cfbStream.Write(TestData.CreateByteArray(streamLength), 0, streamLength);
        return stream.ToArray();
    }

    [TestMethod]
    public void DifatSectorEnumerator_EmptyChain()
    {
        using RootStorage root = CreateRoot();
        using DifatSectorEnumerator enumerator = new(root.ContextSite);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsFalse(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(Sector.EndOfChain, nonGeneric.Current);
    }

    [TestMethod]
    public void DifatSectorEnumerator_ChainLongerThanCount()
    {
        using RootStorage root = CreateRoot();
        Header header = root.Context.Header;
        header.FirstDifatSectorId = 1;
        try
        {
            using DifatSectorEnumerator enumerator = new(root.ContextSite);
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveNext());
        }
        finally
        {
            header.FirstDifatSectorId = SectorType.EndOfChain;
        }
    }

    [TestMethod]
    public void DifatSectorEnumerator_MoveToEmptyChain()
    {
        using RootStorage root = CreateRoot();
        Header header = root.Context.Header;
        header.DifatSectorCount = 1;
        try
        {
            using DifatSectorEnumerator enumerator = new(root.ContextSite);
            Assert.IsFalse(enumerator.MoveTo(0));
        }
        finally
        {
            header.DifatSectorCount = 0;
        }
    }

    [TestMethod]
    public void DifatSectorEnumerator_CountLargerThanChain()
    {
        using RootStorage root = CreateRoot(DifatStreamLength);
        Header header = root.Context.Header;
        uint count = header.DifatSectorCount;
        Assert.IsGreaterThan(0u, count);
        try
        {
            header.DifatSectorCount = count + 5;
            using DifatSectorEnumerator enumerator = new(root.ContextSite);
            Assert.IsTrue(enumerator.MoveTo(count - 1));
            Assert.IsFalse(enumerator.MoveTo(count));
            enumerator.Reset();
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.Add());
        }
        finally
        {
            header.DifatSectorCount = count;
        }
    }

    [TestMethod]
    public void FatSectorEnumerator_EnumerateAndReset()
    {
        using RootStorage root = CreateRoot(DifatStreamLength);
        using FatSectorEnumerator enumerator = new(root.ContextSite);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);

        int count = 0;
        while (enumerator.MoveNext())
            count++;
        Assert.AreEqual((int)root.Context.Header.FatSectorCount, count);
        Assert.IsGreaterThan(109, count);

        enumerator.Reset();
        Assert.IsTrue(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(enumerator.Current, nonGeneric.Current);
    }

    [TestMethod]
    public void FatSectorEnumerator_MoveToMissingDifatSector()
    {
        using RootStorage root = CreateRoot();
        Header header = root.Context.Header;
        uint count = header.FatSectorCount;
        header.FatSectorCount = 500;
        try
        {
            using FatSectorEnumerator enumerator = new(root.ContextSite);
            Assert.IsFalse(enumerator.MoveTo(300));
        }
        finally
        {
            header.FatSectorCount = count;
        }
    }

    [TestMethod]
    public void FatSectorEnumerator_TruncatedDifatSector()
    {
        byte[] bytes = CreateFileBytes(DifatStreamLength);
        using TruncatedReadMemoryStream stream = new(bytes);
        using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        stream.TruncateAt(new Sector(root.Context.Header.FirstDifatSectorId, root.Context.SectorSize).Position + sizeof(uint) / 2);
        using FatSectorEnumerator enumerator = new(root.ContextSite);

        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(Header.DifatArrayLength));

        Assert.IsInstanceOfType<EndOfStreamException>(exception.InnerException);
    }

    [TestMethod]
    public void DifatSectorEnumerator_TruncatedSector()
    {
        byte[] bytes = CreateFileBytes(DifatStreamLength);
        using TruncatedReadMemoryStream stream = new(bytes);
        using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        Sector difatSector = new(root.Context.Header.FirstDifatSectorId, root.Context.SectorSize);
        stream.TruncateAt(difatSector.EndPosition - sizeof(uint) + sizeof(uint) / 2);
        using DifatSectorEnumerator enumerator = new(root.ContextSite);
        Assert.IsTrue(enumerator.MoveNext());

        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveNext());

        Assert.IsInstanceOfType<EndOfStreamException>(exception.InnerException);
    }

    [TestMethod]
    [DataRow(uint.MaxValue)]
    [DataRow(SectorType.Maximum)]
    public void FatSectorEnumerator_MoveToInvalidHeaderDifatEntry(uint id)
    {
        using RootStorage root = CreateRoot();
        Header header = root.Context.Header;
        uint original = header.Difat[0];
        header.Difat[0] = id;
        try
        {
            using FatSectorEnumerator enumerator = new(root.ContextSite);
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(0));
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(0));
        }
        finally
        {
            header.Difat[0] = original;
        }
    }

    [TestMethod]
    public void FatSectorEnumerator_MoveToInvalidDifatSectorEntry()
    {
        using RootStorage root = CreateRoot(DifatStreamLength);
        using FatSectorEnumerator enumerator = new(root.ContextSite);

        using DifatSectorEnumerator difatEnumerator = new(root.ContextSite);
        Assert.IsTrue(difatEnumerator.MoveNext());
        long position = difatEnumerator.Current.Position;

        CfbBinaryReader reader = root.Context.Reader;
        reader.Position = position;
        uint original = reader.ReadUInt32();

        CfbBinaryWriter writer = root.Context.Writer;
        writer.Position = position;
        writer.Write(uint.MaxValue);
        try
        {
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(Header.DifatArrayLength));
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(Header.DifatArrayLength));
        }
        finally
        {
            writer.Position = position;
            writer.Write(original);
        }
    }

    [TestMethod]
    public void Fat_MissingKey()
    {
        using RootStorage root = CreateRoot();
        Fat fat = root.Context.Fat;
        const uint missing = 1_000_000;
        Assert.IsFalse(fat.TryGetValue(missing, out _));
        Assert.IsFalse(fat.TrySetValue(missing, 0));
        Assert.ThrowsExactly<FileFormatException>(() => _ = fat[missing]);
        Assert.ThrowsExactly<FileFormatException>(() => fat[missing] = 0);
    }

    [TestMethod]
    public void Fat_TruncatedSector()
    {
        byte[] bytes = CreateFileBytes(100_000);
        using TruncatedReadMemoryStream stream = new(bytes);
        using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        uint fatSectorId = root.Context.Header.Difat[1];
        stream.TruncateAt(new Sector(fatSectorId, root.Context.SectorSize).Position + sizeof(uint) / 2);

        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            root.Context.Fat.TryGetValue((uint)root.Context.FatEntriesPerSector, out _));

        Assert.IsInstanceOfType<EndOfStreamException>(exception.InnerException);
    }

    [TestMethod]
    public void MiniFat_TruncatedSector()
    {
        byte[] bytes = CreateFileBytes(100);
        using TruncatedReadMemoryStream stream = new(bytes);
        using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        uint miniFatSectorId = root.Context.Header.FirstMiniFatSectorId;
        stream.TruncateAt(new Sector(miniFatSectorId, root.Context.SectorSize).Position + sizeof(uint) / 2);

        FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            root.Context.MiniFat.TryGetValue(0, out _));

        Assert.IsInstanceOfType<EndOfStreamException>(exception.InnerException);
    }

    [TestMethod]
    public void Fat_Enumerate()
    {
        using RootStorage root = CreateRoot();
        Fat fat = root.Context.Fat;
        int count = fat.Count();
        Assert.IsGreaterThan(0, count);

        int nonGenericCount = 0;
        IEnumerator enumerator = ((IEnumerable)fat).GetEnumerator();
        while (enumerator.MoveNext())
        {
            _ = enumerator.Current;
            nonGenericCount++;
        }

        Assert.AreEqual(count, nonGenericCount);
    }

    [TestMethod]
    public void FatEnumerator_MoveNextBeyondMaximum()
    {
        using RootStorage root = CreateRoot();
        using FatEnumerator enumerator = new(root.ContextSite);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsFalse(enumerator.MoveTo(1_000_000));
        Assert.IsFalse(enumerator.MoveNext());
    }

    [TestMethod]
    public void FatEnumerator_ResetAndNonGenericCurrent()
    {
        using RootStorage root = CreateRoot();
        using FatEnumerator enumerator = new(root.ContextSite);
        Assert.IsTrue(enumerator.MoveNext());
        enumerator.Reset();
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(enumerator.Current, nonGeneric.Current);
    }

    [TestMethod]
    public void FatEnumerator_EntryBeyondEndOfStream()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        Fat fat = root.Context.Fat;
        uint original = fat[start];
        try
        {
            fat[start] = root.Context.SectorCount + 1000;
            using FatEnumerator enumerator = new(root.ContextSite);
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveTo(start));

            fat[start] = SectorType.Maximum;
            using FatEnumerator maximumEnumerator = new(root.ContextSite);
            Assert.ThrowsExactly<FileFormatException>(() => maximumEnumerator.MoveTo(start));
        }
        finally
        {
            fat[start] = original;
        }
    }

    [TestMethod]
    public void FatChainEnumerator_CurrentAndRepeatedMoveNext()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        using FatChainEnumerator enumerator = new(root.ContextSite, start);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(start, enumerator.Current);
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(start, nonGeneric.Current);

        while (enumerator.MoveNext())
        {
        }

        Assert.IsFalse(enumerator.MoveNext());
    }

    [TestMethod]
    public void FatChainEnumerator_InvalidSectorId()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        Fat fat = root.Context.Fat;
        uint original = fat[start];
        try
        {
            fat[start] = SectorType.Difat;
            using FatChainEnumerator enumerator = new(root.ContextSite, start);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveNext());
        }
        finally
        {
            fat[start] = original;
        }
    }

    [TestMethod]
    public void FatChainEnumerator_Loop()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        List<uint> chain = GetChain(root, start);
        Fat fat = root.Context.Fat;
        uint second = chain[1];
        try
        {
            fat[second] = start;
            using FatChainEnumerator enumerator = new(root.ContextSite, start);
            FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            {
                while (enumerator.MoveNext())
                {
                }
            });
            StringAssert.Contains(exception.Message, "loop");
        }
        finally
        {
            fat[second] = chain[2];
        }
    }

    [TestMethod]
    public void FatChainEnumerator_LongerThanSectorCount()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        List<uint> chain = GetChain(root, start);
        Fat fat = root.Context.Fat;
        uint last = chain[chain.Count - 1];
        try
        {
            // A loop that is too long to be detected before the chain has more sectors than the file
            fat[last] = start;
            using FatChainEnumerator enumerator = new(root.ContextSite, start);
            FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            {
                while (enumerator.MoveNext())
                {
                }
            });
            StringAssert.Contains(exception.Message, "sector count");
        }
        finally
        {
            fat[last] = SectorType.EndOfChain;
        }
    }

    [TestMethod]
    public void FatChainEnumerator_ExtendAndShrinkValidateLength()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        using FatChainEnumerator enumerator = new(root.ContextSite, start);
        uint length = (uint)enumerator.GetLength();
        Assert.ThrowsExactly<ArgumentException>(() => enumerator.Extend(length));
        Assert.ThrowsExactly<ArgumentException>(() => enumerator.Shrink(length));
    }

    [TestMethod]
    public void FatChainEnumerator_ExtendFromUpdatesLength()
    {
        using RootStorage root = CreateRoot();
        uint start = GetStartSectorId(root);
        using FatChainEnumerator enumerator = new(root.ContextSite, start);
        long length = enumerator.GetLength();

        enumerator.ExtendFrom(0);
        Assert.AreEqual(length + 1, enumerator.GetLength());

        enumerator.ExtendFrom(0);
        Assert.AreEqual(length + 2, enumerator.GetLength());

        enumerator.Shrink((uint)length);
        Assert.AreEqual(length, enumerator.GetLength());
    }

    [TestMethod]
    public void MiniFat_MissingKey()
    {
        using RootStorage root = CreateRoot(100);
        MiniFat miniFat = root.Context.MiniFat;
        const uint missing = 1_000_000;
        Assert.IsFalse(miniFat.TryGetValue(missing, out _));
        Assert.IsFalse(miniFat.TrySetValue(missing, 0));
        Assert.ThrowsExactly<FileFormatException>(() => _ = miniFat[missing]);
        Assert.ThrowsExactly<FileFormatException>(() => miniFat[missing] = 0);
    }

    [TestMethod]
    public void MiniFat_Enumerate()
    {
        using RootStorage root = CreateRoot(100);
        MiniFat miniFat = root.Context.MiniFat;
        int count = miniFat.Count();
        Assert.IsGreaterThan(0, count);

        int nonGenericCount = 0;
        IEnumerator enumerator = ((IEnumerable)miniFat).GetEnumerator();
        while (enumerator.MoveNext())
        {
            _ = enumerator.Current;
            nonGenericCount++;
        }

        Assert.AreEqual(count, nonGenericCount);
    }

    [TestMethod]
    public void MiniFatEnumerator_Empty()
    {
        using var root = RootStorage.CreateInMemory();
        using MiniFatEnumerator enumerator = new(root.ContextSite);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.CurrentSector);
        Assert.IsFalse(enumerator.MoveNextFreeEntry());
        Assert.IsFalse(enumerator.MoveTo(1_000_000));
        Assert.IsFalse(enumerator.MoveNext());
    }

    [TestMethod]
    public void MiniFatEnumerator_NonGenericCurrent()
    {
        using RootStorage root = CreateRoot(100);
        using MiniFatEnumerator enumerator = new(root.ContextSite);
        Assert.IsTrue(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(enumerator.Current, nonGeneric.Current);
    }

    [TestMethod]
    public void MiniFatChainEnumerator_InvalidSectorId()
    {
        using RootStorage root = CreateRoot(100);
        uint start = root.Context.DirectoryEntries.GetDirectoryEntry(1).StartSectorId;
        MiniFat miniFat = root.Context.MiniFat;
        uint original = miniFat[start];
        try
        {
            miniFat[start] = SectorType.Difat;
            using MiniFatChainEnumerator enumerator = new(root.ContextSite, start);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.ThrowsExactly<FileFormatException>(() => enumerator.MoveNext());
        }
        finally
        {
            miniFat[start] = original;
        }
    }

    [TestMethod]
    public void MiniFatChainEnumerator_Loop()
    {
        using RootStorage root = CreateRoot(300);
        uint start = root.Context.DirectoryEntries.GetDirectoryEntry(1).StartSectorId;
        MiniFat miniFat = root.Context.MiniFat;
        List<uint> chain = [];
        using (MiniFatChainEnumerator chainEnumerator = new(root.ContextSite, start))
        {
            while (chainEnumerator.MoveNext())
                chain.Add(chainEnumerator.Current);
        }

        Assert.IsGreaterThan(2, chain.Count);
        uint second = chain[1];
        try
        {
            miniFat[second] = start;
            using MiniFatChainEnumerator enumerator = new(root.ContextSite, start);
            FileFormatException exception = Assert.ThrowsExactly<FileFormatException>(() =>
            {
                while (enumerator.MoveNext())
                {
                }
            });
            StringAssert.Contains(exception.Message, "loop");
        }
        finally
        {
            miniFat[second] = chain[2];
        }
    }

    [TestMethod]
    public void MiniFatChainEnumerator_CurrentAndMoveTo()
    {
        using RootStorage root = CreateRoot(300);
        uint start = root.Context.DirectoryEntries.GetDirectoryEntry(1).StartSectorId;
        using MiniFatChainEnumerator enumerator = new(root.ContextSite, start);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsFalse(enumerator.MoveTo(1000));
        Assert.IsTrue(enumerator.MoveTo(0));
        IEnumerator nonGeneric = enumerator;
        Assert.AreEqual(start, nonGeneric.Current);
    }

    [TestMethod]
    public void MiniFatChainEnumerator_ExtendAndShrinkValidateLength()
    {
        using RootStorage root = CreateRoot(100);
        uint start = root.Context.DirectoryEntries.GetDirectoryEntry(1).StartSectorId;
        using MiniFatChainEnumerator enumerator = new(root.ContextSite, start);
        uint length = (uint)enumerator.GetLength();
        Assert.ThrowsExactly<ArgumentException>(() => enumerator.Extend(length));
        Assert.ThrowsExactly<ArgumentException>(() => enumerator.Shrink(length));
    }

    [TestMethod]
    public void DirectoryEntryEnumerator_NonGenericCurrent()
    {
        using RootStorage root = CreateRoot();
        using DirectoryEntryEnumerator enumerator = new(root.Context.DirectoryEntries);
        Assert.IsTrue(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreSame(enumerator.Current, nonGeneric.Current);
    }

    [TestMethod]
    public void DirectoryTreeEnumerator_Current()
    {
        using RootStorage root = CreateRoot();
        using DirectoryTreeEnumerator enumerator = new(root.ContextSite, StreamId.Root);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        IEnumerator nonGeneric = enumerator;
        Assert.AreSame(enumerator.Current, nonGeneric.Current);
    }

    [TestMethod]
    public void DirectoryTree_RemoveEntryNotInTree()
    {
        using var root = RootStorage.CreateInMemory();
        DirectoryTree tree = new(root.ContextSite, StreamId.Root);
        DirectoryEntry missing = new() { Id = 5, NameString = "b" };

        // Empty tree
        Assert.ThrowsExactly<FileFormatException>(() => tree.Remove(missing));

        // Entries on both sides of the missing entry
        using (root.CreateStream("a"))
        {
        }

        using (root.CreateStream("c"))
        {
        }

        Assert.ThrowsExactly<FileFormatException>(() => tree.Remove(missing));
        DirectoryEntry greater = new() { Id = 5, NameString = "d" };
        Assert.ThrowsExactly<FileFormatException>(() => tree.Remove(greater));
    }

    [TestMethod]
    public void DirectoryTree_Empty()
    {
        using var root = RootStorage.CreateInMemory();
        DirectoryTree tree = new(root.ContextSite, StreamId.Root);
        Assert.IsFalse(tree.TryGetDirectoryEntry("missing", out DirectoryEntry? entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public void ChainCheckpointTable_SelfThinning()
    {
        ChainCheckpointTable table = default;
        Assert.AreEqual(0, table.Count);
        Assert.AreEqual(1u, table.Stride);
        Assert.IsFalse(table.TryGetCheckpoint(0, out _, out _));

        uint max = ChainCheckpointTable.MaxCount;

        // Fill initial capacity (0..MaxCount-1) at stride 1
        for (uint i = 0; i < max; i++)
            table.Add(i, 1000 + i);

        Assert.AreEqual((int)max, table.Count);
        Assert.AreEqual(1u, table.Stride);
        Assert.IsTrue(table.TryGetCheckpoint(max - 1, out uint cpLast, out uint sectorLast));
        Assert.AreEqual(max - 1, cpLast);
        Assert.AreEqual(1000 + max - 1, sectorLast);

        // Adding index MaxCount thins the table to stride 2 (keeping MaxCount/2 even indices and adding MaxCount)
        table.Add(max, 1000 + max);
        Assert.AreEqual((int)(max / 2) + 1, table.Count);
        Assert.AreEqual(2u, table.Stride);

        // Non-multiple of stride 2 is ignored
        table.Add(max + 1, 1000 + max + 1);
        Assert.AreEqual((int)(max / 2) + 1, table.Count);

        // Continue adding up to 2 * MaxCount, which triggers a second thinning to stride 4
        for (uint i = max + 2; i <= max * 2; i++)
            table.Add(i, 1000 + i);

        Assert.AreEqual((int)(max / 2) + 1, table.Count);
        Assert.AreEqual(4u, table.Stride);

        Assert.IsTrue(table.TryGetCheckpoint(7, out uint cp7, out uint s7));
        Assert.AreEqual(4u, cp7);
        Assert.AreEqual(1004u, s7);

        Assert.IsTrue(table.TryGetCheckpoint(max * 3, out uint cpBeyond, out uint sBeyond));
        Assert.AreEqual(max * 2, cpBeyond);
        Assert.AreEqual(1000 + (max * 2), sBeyond);

        table.Clear();
        Assert.AreEqual(0, table.Count);
        Assert.AreEqual(1u, table.Stride);
    }

    [TestMethod]
    public void FatChainEnumerator_BackwardMoveTo()
    {
        using RootStorage root = CreateRoot(25 * 512);
        uint start = GetStartSectorId(root);
        List<uint> expected = GetChain(root, start);
        Assert.AreEqual(25, expected.Count);

        using FatChainEnumerator enumerator = new(root.ContextSite, start);
        Assert.IsTrue(enumerator.MoveTo((uint)(expected.Count - 1)));

        // Walk backward through every index and verify exact sector IDs
        for (int i = expected.Count - 1; i >= 0; i--)
        {
            Assert.IsTrue(enumerator.MoveTo((uint)i));
            Assert.AreEqual(expected[i], enumerator.Current);
        }

        // Walk forward via MoveNext across the already-discovered range
        enumerator.Reset();
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(expected[i], enumerator.Current);
        }
        Assert.IsFalse(enumerator.MoveNext());

        // Reset with the same start sector keeps checkpoints; Reset with a different start sector switches chains
        enumerator.Reset(start);
        Assert.IsTrue(enumerator.MoveTo((uint)(expected.Count - 1)));
        Assert.AreEqual(expected[expected.Count - 1], enumerator.Current);

        enumerator.Reset(expected[1]);
        Assert.IsTrue(enumerator.MoveTo(0));
        Assert.AreEqual(expected[1], enumerator.Current);
    }

    [TestMethod]
    public void MiniFatChainEnumerator_BackwardMoveTo()
    {
        using RootStorage root = CreateRoot(25 * 64);
        uint start = root.Context.DirectoryEntries.GetDirectoryEntry(1).StartSectorId;
        List<uint> expected = [];
        using (MiniFatChainEnumerator full = new(root.ContextSite, start))
        {
            while (full.MoveNext())
                expected.Add(full.Current);
        }
        Assert.AreEqual(25, expected.Count);

        using MiniFatChainEnumerator enumerator = new(root.ContextSite, start);
        Assert.IsTrue(enumerator.MoveTo((uint)(expected.Count - 1)));

        for (int i = expected.Count - 1; i >= 0; i--)
        {
            Assert.IsTrue(enumerator.MoveTo((uint)i));
            Assert.AreEqual(expected[i], enumerator.Current);
        }

        enumerator.Reset();
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(expected[i], enumerator.Current);
        }
        Assert.IsFalse(enumerator.MoveNext());
    }

    [TestMethod]
    public void DifatSectorEnumerator_BackwardMoveTo()
    {
        // 16 MB stream requires 2 DIFAT sectors in V3
        using RootStorage root = CreateRoot(16_000_000);
        uint difatCount = root.Context.Header.DifatSectorCount;
        Assert.IsGreaterThanOrEqualTo(2u, difatCount);

        using DifatSectorEnumerator enumerator = new(root.ContextSite);
        Assert.IsTrue(enumerator.MoveTo(difatCount - 1));
        Sector last = enumerator.Current;

        Assert.IsTrue(enumerator.MoveTo(0));
        Assert.AreEqual(root.Context.Header.FirstDifatSectorId, enumerator.Current.Id);

        Assert.IsTrue(enumerator.MoveNext());
        Assert.IsTrue(enumerator.MoveTo(difatCount - 1));
        Assert.AreEqual(last, enumerator.Current);
    }

    [TestMethod]
    [DataRow(1748)] // ~0.9 MB V3 file (6,991 entries)
    [DataRow(15000)] // ~7.7 MB V3 file (59,999 entries, exercises DIFAT-backed FAT sectors)
    public void CraftedDirectoryLayout_LinearReads(int directorySectors)
    {
        byte[] craftedBytes = BuildCraftedDirectoryFile(directorySectors);
        using CountingReadStream stream = new(craftedBytes);
        using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation);

        int count = 0;
        foreach (EntryInfo entry in root.EnumerateEntries())
        {
            _ = entry;
            count++;
        }

        Assert.AreEqual(4 * directorySectors - 1, count);

        // Without checkpoint indexing, 1748 sectors reads 3,465x the file and 15000 sectors reads >30,000x.
        // With checkpoint indexing, total bytes requested stays under 2x the file length.
        Assert.IsLessThanOrEqualTo(craftedBytes.Length * 2L, stream.BytesRead);

        long bytesBeforeLookup = stream.BytesRead;
        Assert.IsFalse(root.TryOpenStream("zzzzzzzzz", out _));

        // Every directory sector is already indexed in memory, so walking the chain again reads 0 FAT sectors
        // and requests at most 1x the directory size.
        long lookupBytes = stream.BytesRead - bytesBeforeLookup;
        Assert.IsLessThanOrEqualTo(craftedBytes.Length, lookupBytes);
    }

    static byte[] BuildCraftedDirectoryFile(int d)
    {
        const int SectorSize = 512, FatPerSector = 128, DifatPerSector = 127, HeaderDifat = 109, EntriesPerSector = 4;
        const uint EndOfChain = 0xFFFFFFFE, Free = 0xFFFFFFFF, FatSector = 0xFFFFFFFD, DifatSector = 0xFFFFFFFC, NoStream = 0xFFFFFFFF;

        int f = 1, df = 0;
        for (; ;)
        {
            int needF = (f + df + d + FatPerSector - 1) / FatPerSector;
            int needDf = needF > HeaderDifat ? (needF - HeaderDifat + DifatPerSector - 1) / DifatPerSector : 0;
            if (needF == f && needDf == df)
                break;
            (f, df) = (needF, needDf);
        }
        int dirBase = f + df;

        int stride = 131;
        while (Gcd(stride, d) != 1)
            stride++;

        uint Phys(int chainIndex) => (uint)(dirBase + (int)((long)chainIndex * stride % d));
        long SectorOffset(uint id) => (id + 1L) * SectorSize;
        long EntryOffset(uint sid) => SectorOffset(Phys((int)(sid / EntriesPerSector))) + sid % EntriesPerSector * 128;

        byte[] b = new byte[(f + df + d + 1) * SectorSize];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(b, 0);
        U16(b, 24, 0x003E);
        U16(b, 26, 3);
        U16(b, 28, 0xFFFE);
        U16(b, 30, 9);
        U16(b, 32, 6);
        U32(b, 44, (uint)f);
        U32(b, 48, Phys(0));
        U32(b, 56, 4096);
        U32(b, 60, EndOfChain);
        U32(b, 68, df > 0 ? (uint)f : EndOfChain);
        U32(b, 72, (uint)df);

        for (int i = 0; i < HeaderDifat; i++)
            U32(b, 76 + i * 4, i < f ? (uint)i : Free);

        for (int k = 0; k < df; k++)
        {
            long off = SectorOffset((uint)(f + k));
            for (int j = 0; j < DifatPerSector; j++)
            {
                int fatIndex = HeaderDifat + k * DifatPerSector + j;
                U32(b, off + j * 4, fatIndex < f ? (uint)fatIndex : Free);
            }
            U32(b, off + DifatPerSector * 4, k + 1 < df ? (uint)(f + k + 1) : EndOfChain);
        }

        long FatOffset(uint sector) => SectorOffset(sector / FatPerSector) + sector % FatPerSector * 4;
        for (uint i = 0; i < (uint)f * FatPerSector; i++)
            U32(b, FatOffset(i), Free);
        for (uint i = 0; i < f; i++)
            U32(b, FatOffset(i), FatSector);
        for (uint i = 0; i < df; i++)
            U32(b, FatOffset((uint)f + i), DifatSector);
        for (int i = 0; i < d; i++)
            U32(b, FatOffset(Phys(i)), i == d - 1 ? EndOfChain : Phys(i + 1));

        int count = EntriesPerSector * d - 1;
        uint[] sidOfRank = new uint[count];
        int r = 0;
        for (int slot = 0; slot < EntriesPerSector; slot++)
        {
            for (int sector = d - 1; sector >= 0; sector--)
            {
                if (sector != 0 || slot != 0)
                    sidOfRank[r++] = (uint)(sector * EntriesPerSector + slot);
            }
        }

        WriteEntry(b, EntryOffset(0), "Root Entry", type: 5, right: NoStream, child: sidOfRank[0]);
        for (int i = 0; i < count; i++)
            WriteEntry(b, EntryOffset(sidOfRank[i]), "n" + i.ToString("D7"), type: 2, right: i + 1 < count ? sidOfRank[i + 1] : NoStream, child: NoStream);

        return b;

        static void WriteEntry(byte[] b, long off, string name, byte type, uint right, uint child)
        {
            Encoding.Unicode.GetBytes(name).CopyTo(b, (int)off);
            U16(b, off + 64, (ushort)((name.Length + 1) * 2));
            b[off + 66] = type;
            b[off + 67] = 1;
            U32(b, off + 68, NoStream);
            U32(b, off + 72, right);
            U32(b, off + 76, child);
            U32(b, off + 116, EndOfChain);
        }

        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        static void U16(byte[] b, long off, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan((int)off, 2), v);
        static void U32(byte[] b, long off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan((int)off, 4), v);
    }

    sealed class CountingReadStream : Stream
    {
        readonly byte[] data;
        long position;

        public CountingReadStream(byte[] data)
        {
            this.data = data;
        }

        public long Reads { get; private set; }

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Max(0, Math.Min(count, data.Length - position));
            Array.Copy(data, position, buffer, offset, n);
            position += n;
            Reads++;
            BytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => data.Length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    sealed class TruncatedReadMemoryStream : MemoryStream
    {
        long truncatedAt = long.MaxValue;

        public TruncatedReadMemoryStream(byte[] buffer)
            : base(buffer, writable: false)
        {
        }

        public void TruncateAt(long position) => truncatedAt = position;

        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, (int)Math.Min(count, Math.Max(0, truncatedAt - Position)));

#if NET8_0_OR_GREATER
        public override int Read(Span<byte> buffer)
            => base.Read(buffer[..(int)Math.Min(buffer.Length, Math.Max(0, truncatedAt - Position))]);
#endif

        public override int ReadByte() => Position < truncatedAt ? base.ReadByte() : -1;
    }
}
