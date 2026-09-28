using System.Collections;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="Sector"/>s in a DIFAT chain.
/// </summary>
internal sealed class DifatSectorEnumerator : ContextBase, IEnumerator<Sector>
{
    bool started = false;
    uint index = uint.MaxValue;
    Sector current = Sector.EndOfChain;
    private uint difatSectorId = SectorType.EndOfChain;

    public DifatSectorEnumerator(RootContextSite rootContextSite)
        : base(rootContextSite)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
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
            difatSectorId = Context.Header.FirstDifatSectorId;
        }
        else if (difatSectorId != SectorType.EndOfChain)
        {
            CfbBinaryReader reader = Context.Reader;
            reader.Position = current.EndPosition - sizeof(uint);
            difatSectorId = reader.ReadUInt32();
            if (difatSectorId != SectorType.EndOfChain)
                ThrowHelper.ThrowIfSectorIdIsInvalid(difatSectorId);
        }

        if (difatSectorId == SectorType.EndOfChain)
        {
            index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        uint nextIndex = index + 1;
        if (nextIndex >= Context.Header.DifatSectorCount)
            throw new FileFormatException("DIFAT chain index is greater than the sector count.");
        if (nextIndex >= Context.SectorCount)
            throw new FileFormatException("DIFAT chain index is greater than the maximum for the stream length.");

        current = new(difatSectorId, Context.SectorSize);
        index = nextIndex;
        return true;
    }

    public bool MoveTo(uint index)
    {
        if (index >= Context.Header.DifatSectorCount)
            return false;

        if (!started && !MoveNext())
            return false;

        if (index < this.index)
            Reset();

        while (!started || this.index < index)
        {
            if (!MoveNext())
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        index = uint.MaxValue;
        current = Sector.EndOfChain;
        difatSectorId = SectorType.EndOfChain;
    }

    public void Add()
    {
        Sector newDifatSector = new(Context.SectorCount, Context.SectorSize);

        Header header = Context.Header;
        CfbBinaryWriter writer = Context.Writer;
        if (header.FirstDifatSectorId == SectorType.EndOfChain)
        {
            header.FirstDifatSectorId = newDifatSector.Id;
        }
        else
        {
            bool ok = MoveTo(header.DifatSectorCount - 1);
            if (!ok)
                throw new FileFormatException("The DIFAT sector count is invalid.");

            writer.Position = current.EndPosition - sizeof(uint);
            writer.Write(newDifatSector.Id);
        }

        writer.Position = newDifatSector.Position;
        writer.Write(SectorDataCache.GetFatEntryData(newDifatSector.Length));
        writer.Position = newDifatSector.EndPosition - sizeof(uint);
        writer.Write(SectorType.EndOfChain);

        Context.ExtendStreamLength(newDifatSector.EndPosition);
        header.DifatSectorCount++;

        Context.Fat[newDifatSector.Id] = SectorType.Difat;

        started = true;
        index = header.DifatSectorCount - 1;
        current = newDifatSector;
        difatSectorId = SectorType.EndOfChain;
    }
}
