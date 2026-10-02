using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Encapsulates getting and setting entries in the FAT.
/// </summary>
internal sealed class Fat : ContextBase, IEnumerable<FatEntry>, IDisposable
{
    private readonly FatSectorEnumerator fatSectorEnumerator;
    private readonly byte[] cachedSectorBuffer;
    Sector cachedSector = Sector.EndOfChain;
    private bool isDirty;

    public Func<FatEntry, bool> IsUsed { get; }

    public Fat(RootContextSite rootContextSite)
        : base(rootContextSite)
    {
        fatSectorEnumerator = new(rootContextSite);
        cachedSectorBuffer = new byte[Context.SectorSize];

        IsUsed = Context.Version switch
        {
            Version.V3 => entry => entry.Value is not SectorType.Free,
            Version.V4 => entry => entry.Value is not SectorType.Free && entry.Index is not RootContext.RangeLockSectorId,
            _ => throw new NotSupportedException($"Unsupported major version: {Context.Version}."),
        };
    }

    public void Dispose()
    {
        Flush();

        fatSectorEnumerator.Dispose();
    }

    public uint this[uint key]
    {
        get
        {
            if (!TryGetValue(key, out uint value))
                throw new FileFormatException($"FAT index not found: {key}.");
            return value;
        }

        set
        {
            if (!TrySetValue(key, value))
                throw new FileFormatException($"FAT index not found: {key}.");
        }
    }

    uint GetSectorIndexAndElementOffset(uint key, out long elementIndex) => (uint)Math.DivRem(key, Context.FatEntriesPerSector, out elementIndex);

    void CacheCurrentSector()
    {
        Sector current = fatSectorEnumerator.Current;
        if (cachedSector.Id == current.Id)
            return;

        Flush();

        Stream stream = Context.Stream;
        stream.Position = current.Position;
        stream.ReadExactly(cachedSectorBuffer, 0, cachedSectorBuffer.Length);
        cachedSector = current;
    }

    public void Flush()
    {
        if (isDirty)
        {
            Stream stream = Context.Stream;
            stream.Position = cachedSector.Position;
            stream.Write(cachedSectorBuffer, 0, cachedSectorBuffer.Length);
            isDirty = false;
        }
    }

    bool TryMoveToSectorForKey(uint key, out long offset)
    {
        uint sectorId = GetSectorIndexAndElementOffset(key, out offset);
        bool ok = fatSectorEnumerator.MoveTo(sectorId);
        if (!ok)
            return false;

        CacheCurrentSector();
        return true;
    }

    public bool TryGetValue(uint key, out uint value)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(key);

        bool ok = TryMoveToSectorForKey(key, out long elementIndex);
        if (!ok)
        {
            value = uint.MaxValue;
            return false;
        }

        ReadOnlySpan<byte> slice = cachedSectorBuffer.AsSpan((int)elementIndex * sizeof(uint));
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

    /// <summary>
    /// Adds a new entry to the FAT.
    /// </summary>
    /// <returns>The index of the new entry in the FAT.</returns>
    public uint Add(FatEnumerator fatEnumerator, uint startIndex)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(startIndex);

        bool movedToFreeEntry = fatEnumerator.MoveTo(startIndex)
            && fatEnumerator.MoveNextFreeEntry();
        if (!movedToFreeEntry)
        {
            uint newSectorId = fatSectorEnumerator.Add();

            // Next id must be free
            bool ok = fatEnumerator.MoveTo(newSectorId);
            Debug.Assert(ok);

            ok = fatEnumerator.MoveNextFreeEntry();
            Debug.Assert(ok);
        }

        FatEntry entry = fatEnumerator.Current;
        Sector sector = new(entry.Index, Context.SectorSize);
        Context.ExtendStreamLength(sector.EndPosition);
        this[entry.Index] = SectorType.EndOfChain;
        return entry.Index;
    }

    public Sector GetLastUsedSector()
    {
        FatEntry lastUsedSectorIndex = new(uint.MaxValue, uint.MaxValue);
        foreach (FatEntry entry in this)
        {
            if (IsUsed(entry))
                lastUsedSectorIndex = entry;
        }

        return new(lastUsedSectorIndex.Index, Context.SectorSize);
    }

    public IEnumerator<FatEntry> GetEnumerator() => new FatEnumerator(ContextSite);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [ExcludeFromCodeCoverage]
    internal void WriteTrace(TextWriter writer)
    {
        byte[] data = new byte[Context.SectorSize];

        Stream baseStream = Context.Reader.BaseStream;

        writer.WriteLine("Start of FAT =================");

        long freeCount = 0;
        long usedCount = 0;

        foreach (FatEntry entry in this)
        {
            Sector sector = new(entry.Index, Context.SectorSize);
            if (entry.Value is SectorType.Free)
            {
                freeCount++;
                writer.WriteLine($"{entry}");
            }
            else
            {
                usedCount++;
                baseStream.Position = sector.Position;
                baseStream.ReadExactly(data, 0, data.Length);
                string hex = BitConverter.ToString(data);
                writer.WriteLine($"{entry}: {hex}");
            }
        }

        writer.WriteLine("End of FAT ===================");
        writer.WriteLine();
        writer.WriteLine($"Free sectors: {freeCount}");
        writer.WriteLine($"Used sectors: {usedCount}");
    }

    [ExcludeFromCodeCoverage]
    internal void Validate()
    {
        long fatSectorCount = 0;
        long difatSectorCount = 0;
        foreach (FatEntry entry in this)
        {
            Sector sector = new(entry.Index, Context.SectorSize);
            if (entry.Value <= SectorType.Maximum && sector.EndPosition > Context.Length)
                throw new FileFormatException($"FAT entry {entry} is beyond the end of the stream.");
            if (entry.Value == SectorType.Fat)
                fatSectorCount++;
            if (entry.Value == SectorType.Difat)
                difatSectorCount++;
        }

        if (Context.Header.FatSectorCount != fatSectorCount)
            throw new FileFormatException($"FAT sector count mismatch. Expected: {Context.Header.FatSectorCount} Actual: {fatSectorCount}.");
        if (Context.Header.DifatSectorCount != difatSectorCount)
            throw new FileFormatException($"DIFAT sector count mismatch: Expected: {Context.Header.DifatSectorCount} Actual: {difatSectorCount}.");

        if (Context.Length < RootContext.RangeLockSectorOffset)
        {
            if (this.TryGetValue(RootContext.RangeLockSectorId, out uint value) && value != SectorType.Free)
                throw new FileFormatException($"Range lock FAT entry is not free.");
        }
        else
        {
            if (this[RootContext.RangeLockSectorId] != SectorType.EndOfChain)
                throw new FileFormatException($"Range lock sector is not at the end of the chain.");
        }
    }

    [ExcludeFromCodeCoverage]
    internal long GetFreeSectorCount() => this.Count(entry => entry.Value == SectorType.Free);
}
