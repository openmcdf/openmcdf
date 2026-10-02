using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Encapsulates getting and setting <see cref="FatEntry"/> records in the mini FAT.
/// </summary>
internal sealed class MiniFat : ContextBase, IEnumerable<FatEntry>, IDisposable
{
    private readonly FatChainEnumerator fatChainEnumerator;
    private readonly int elementsPerSector;
    private readonly byte[] cachedSectorBuffer;
    private bool isDirty;

    public MiniFat(RootContextSite rootContextSite)
        : base(rootContextSite)
    {
        elementsPerSector = Context.SectorSize / sizeof(uint);
        fatChainEnumerator = new(ContextSite, Context.Header.FirstMiniFatSectorId);
        cachedSectorBuffer = new byte[Context.SectorSize];
    }

    public void Dispose()
    {
        Flush();

        fatChainEnumerator.Dispose();
    }

    public void Flush()
    {
        if (isDirty)
        {
            CfbBinaryWriter writer = Context.Writer;
            writer.Position = fatChainEnumerator.CurrentSector.Position;
            writer.Write(cachedSectorBuffer);
            isDirty = false;
        }
    }

    public IEnumerator<FatEntry> GetEnumerator() => new MiniFatEnumerator(ContextSite);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public uint this[uint key]
    {
        get
        {
            if (!TryGetValue(key, out uint value))
                throw new FileFormatException($"Mini FAT index not found: {key}.");
            return value;
        }

        set
        {
            if (!TrySetValue(key, value))
                throw new FileFormatException($"Mini FAT index not found: {key}.");
        }
    }

    bool TryMoveToSectorForKey(uint key, out long elementIndex)
    {
        uint fatChain = (uint)Math.DivRem(key, elementsPerSector, out elementIndex);
        if (fatChainEnumerator.IsAt(fatChain))
            return true;

        Flush();

        bool ok = fatChainEnumerator.MoveTo(fatChain);
        if (!ok)
            return false;

        Stream stream = Context.Stream;
        stream.Position = fatChainEnumerator.CurrentSector.Position;
        stream.ReadExactly(cachedSectorBuffer, 0, cachedSectorBuffer.Length);
        return true;
    }

    public bool TryGetValue(uint key, out uint value)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(key);

        if (!TryMoveToSectorForKey(key, out long elementIndex))
        {
            value = uint.MaxValue;
            return false;
        }

        Span<byte> slice = cachedSectorBuffer.AsSpan((int)elementIndex * sizeof(uint));
        value = BinaryPrimitives.ReadUInt32LittleEndian(slice);
        return true;
    }

    public bool TrySetValue(uint key, uint value)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(key);

        if (!TryMoveToSectorForKey(key, out long elementIndex))
            return false;

        Span<byte> slice = cachedSectorBuffer.AsSpan((int)elementIndex * sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(slice, value);
        isDirty = true;
        return true;
    }

    public uint Add(MiniFatEnumerator miniFatEnumerator, uint startIndex)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(startIndex);

        bool movedToFreeEntry = miniFatEnumerator.MoveTo(startIndex) && miniFatEnumerator.MoveNextFreeEntry();
        if (!movedToFreeEntry)
        {
            uint newSectorIndex = fatChainEnumerator.Extend();
            Sector sector = new(newSectorIndex, Context.SectorSize);
            CfbBinaryWriter writer = Context.Writer;
            writer.Position = sector.Position;
            writer.Write(SectorDataCache.GetFatEntryData(sector.Length));

            Header header = Context.Header;
            if (header.FirstMiniFatSectorId == SectorType.EndOfChain)
                header.FirstMiniFatSectorId = newSectorIndex;
            header.MiniFatSectorCount++;

            miniFatEnumerator.Reset(); // TODO: Jump closer to the new sector

            bool ok = miniFatEnumerator.MoveNextFreeEntry();
            Debug.Assert(ok, "No free mini FAT entries found.");
        }

        FatEntry entry = miniFatEnumerator.Current;
        this[entry.Index] = SectorType.EndOfChain;

        Debug.Assert(entry.Value is SectorType.Free);
        MiniSector miniSector = new(entry.Index, Context.MiniSectorSize);
        if (Context.MiniStream.Length < miniSector.EndPosition)
            Context.MiniStream.SetLength(miniSector.EndPosition);

        return entry.Index;
    }

    [ExcludeFromCodeCoverage]
    internal void WriteTrace(TextWriter writer)
    {
        using MiniFatEnumerator miniFatEnumerator = new(ContextSite);

        writer.WriteLine("Start of Mini FAT ============");
        while (miniFatEnumerator.MoveNext())
            writer.WriteLine($"{miniFatEnumerator.Current}");
        writer.WriteLine("End of Mini FAT ==============");
    }

    [ExcludeFromCodeCoverage]
    internal void Validate()
    {
        using MiniFatEnumerator miniFatEnumerator = new(ContextSite);

        while (miniFatEnumerator.MoveNext())
        {
            FatEntry current = miniFatEnumerator.Current;
            if (current.Value <= SectorType.Maximum && miniFatEnumerator.CurrentSector.EndPosition > Context.MiniStream.Length)
            {
                throw new FileFormatException($"Mini FAT entry {current} is beyond the end of the mini stream.");
            }
        }
    }
}
