using System.Collections;

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
}
