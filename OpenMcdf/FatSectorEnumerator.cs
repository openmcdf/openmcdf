using System.Collections;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="Sector"/>s of a compound file.
/// </summary>
internal sealed class FatSectorEnumerator : ContextBase, IEnumerator<Sector>
{
    private readonly DifatSectorEnumerator difatSectorEnumerator;
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

        uint difatChainIndex = GetDifatChainIndexAndDifatEntryIndex(index, out long difatElementIndex);
        if (!difatSectorEnumerator.MoveTo(difatChainIndex))
        {
            this.index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        Sector difatSector = difatSectorEnumerator.Current;
        Context.Reader.Position = difatSector.Position + (difatElementIndex * sizeof(uint));
        uint id = Context.Reader.ReadUInt32();
        ThrowIfFatSectorIdIsInvalid(id);
        this.index = index;
        current = new Sector(id, Context.SectorSize);
        return true;
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
        }

        Context.Fat[newFatSector.Id] = SectorType.Fat;
        return newFatSector.Id;
    }
}
