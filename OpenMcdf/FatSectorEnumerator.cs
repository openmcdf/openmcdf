using System.Collections;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="Sector"/>s of a compound file.
/// </summary>
internal sealed class FatSectorEnumerator : ContextBase, IEnumerator<Sector>
{
    private readonly DifatSectorEnumerator difatSectorEnumerator;
    private uint[]? cachedDifatEntries;
    private bool started = false;
    private uint index = uint.MaxValue;
    private Sector current = Sector.EndOfChain;

    public FatSectorEnumerator(RootContextSite rootContextSite)
        : base(rootContextSite)
    {
        difatSectorEnumerator = new(rootContextSite);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Context is owned by a parent
        difatSectorEnumerator.Dispose();
    }

    /// <inheritdoc/>
    public Sector Current
    {
        get
        {
            ThrowHelper.ThrowIfEnumerationNotStarted(started);
            return current;
        }
    }

    /// <inheritdoc/>
    object IEnumerator.Current => Current;

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (!started)
        {
            started = true;
            index = uint.MaxValue;
        }

        uint nextIndex = index + 1;
        return MoveTo(nextIndex);
    }

    /// <summary>
    /// Moves the enumerator to the specified sector.
    /// </summary>
    public bool MoveTo(uint index)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(index);

        started = true;

        if (index == this.index)
            return true;

        if (index >= Context.Header.FatSectorCount)
        {
            this.index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        if (index < Header.DifatArrayLength)
        {
            uint difatId = Context.Header.Difat[index];
            ThrowIfFatSectorIdIsInvalid(difatId);
            this.index = index;
            current = new(difatId, Context.SectorSize);
            return true;
        }

        uint difatEntryOffset = index - Header.DifatArrayLength;
        if (TryGetCachedDifatEntry(difatEntryOffset, out uint cachedId))
        {
            this.index = index;
            current = new(cachedId, Context.SectorSize);
            return true;
        }

        uint difatChainIndex = GetDifatChainIndexAndDifatEntryIndex(index, out long difatElementIndex);
        if (!difatSectorEnumerator.MoveTo(difatChainIndex))
        {
            this.index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        Sector difatSector = difatSectorEnumerator.Current;
        uint id;
        try
        {
            Context.Reader.Position = difatSector.Position + (difatElementIndex * sizeof(uint));
            id = Context.Reader.ReadUInt32();
        }
        catch (EndOfStreamException exception)
        {
            throw new FileFormatException("DIFAT sector is truncated.", exception);
        }
        ThrowIfFatSectorIdIsInvalid(id);
        CacheDifatEntry(difatEntryOffset, id);
        this.index = index;
        current = new Sector(id, Context.SectorSize);
        return true;
    }

    private bool TryGetCachedDifatEntry(uint offset, out uint sectorId)
    {
        if (cachedDifatEntries is not null && offset < (uint)cachedDifatEntries.Length)
        {
            uint id = cachedDifatEntries[(int)offset];
            if (id != SectorType.Free)
            {
                sectorId = id;
                return true;
            }
        }

        sectorId = SectorType.Free;
        return false;
    }

    private void CacheDifatEntry(uint offset, uint sectorId)
    {
        if (offset >= ChainCheckpointTable.MaxCount)
            return;

        uint maxEntries = Math.Min(ChainCheckpointTable.MaxCount, Context.SectorCount);
        if (offset >= maxEntries)
            return;

        if (cachedDifatEntries is null)
        {
            int initialLength = (int)Math.Min(Math.Max(16u, offset + 1), maxEntries);
            while (initialLength <= offset)
                initialLength = (int)Math.Min((uint)initialLength * 2, maxEntries);

            cachedDifatEntries = new uint[initialLength];
            for (int i = 0; i < cachedDifatEntries.Length; i++)
                cachedDifatEntries[i] = SectorType.Free;
        }
        else if (offset >= (uint)cachedDifatEntries.Length)
        {
            int oldLength = cachedDifatEntries.Length;
            int newLength = oldLength;
            while (newLength <= offset)
                newLength = (int)Math.Min((uint)newLength * 2, maxEntries);

            Array.Resize(ref cachedDifatEntries, newLength);
            for (int i = oldLength; i < newLength; i++)
                cachedDifatEntries[i] = SectorType.Free;
        }

        cachedDifatEntries[(int)offset] = sectorId;
    }

    private void ThrowIfFatSectorIdIsInvalid(uint id)
    {
        if (id >= Context.SectorCount)
            throw new FileFormatException($"FAT sector ID {id:X8} is beyond the end of the stream.");
    }

    private uint GetDifatChainIndexAndDifatEntryIndex(uint index, out long difatElementIndex)
        => (uint)Math.DivRem(index - Header.DifatArrayLength, Context.DifatEntriesPerSector, out difatElementIndex);

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        index = uint.MaxValue;
        current = Sector.EndOfChain;
        difatSectorEnumerator.Reset();
    }

    /// <summary>
    /// Extends the FAT by adding a new sector.
    /// </summary>
    /// <returns>The ID of the new sector that was added.</returns>
    public uint Add()
    {
        Header header = Context.Header;
        uint nextIndex = Context.Header.FatSectorCount;
        Sector newFatSector = new(Context.SectorCount, Context.SectorSize);

        CfbBinaryWriter writer = Context.Writer;
        writer.Position = newFatSector.Position;
        writer.Write(SectorDataCache.GetFatEntryData(newFatSector.Length));
        Context.ExtendStreamLength(newFatSector.EndPosition);

        header.FatSectorCount++;

        index = nextIndex;
        current = newFatSector;

        if (nextIndex < Header.DifatArrayLength)
        {
            header.Difat[nextIndex] = newFatSector.Id;
        }
        else
        {
            uint difatSectorIndex = GetDifatChainIndexAndDifatEntryIndex(nextIndex, out long difatElementIndex);
            if (!difatSectorEnumerator.MoveTo(difatSectorIndex))
                difatSectorEnumerator.Add();

            Sector difatSector = difatSectorEnumerator.Current;
            writer.Position = difatSector.Position + difatElementIndex * sizeof(uint);
            writer.Write(newFatSector.Id);
            CacheDifatEntry(nextIndex - Header.DifatArrayLength, newFatSector.Id);
        }

        Context.Fat[newFatSector.Id] = SectorType.Fat;
        return newFatSector.Id;
    }
}
