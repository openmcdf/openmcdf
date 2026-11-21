using System.Collections;
using System.Diagnostics;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="Sector"/>s in a DIFAT chain.
/// </summary>
internal sealed class DifatSectorEnumerator : ContextBase, IEnumerator<Sector>
{
    private ChainCheckpointTable checkpoints;
    private bool started = false;
    private uint index = uint.MaxValue;
    private Sector current = Sector.EndOfChain;

    // Discovery frontier
    private bool hasFrontier = false;
    private bool endReached = false;
    private uint frontierIndex = uint.MaxValue;
    private Sector frontierCurrent = Sector.EndOfChain;

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
            if (!TryInitializeFrontier())
            {
                index = uint.MaxValue;
                current = Sector.EndOfChain;
                return false;
            }

            index = 0;
            current = new(Context.Header.FirstDifatSectorId, Context.SectorSize);
            return true;
        }

        if (index == uint.MaxValue)
            return false;

        if (index < frontierIndex)
        {
            uint nextIndex = index + 1;
            if (nextIndex == frontierIndex)
            {
                index = frontierIndex;
                current = frontierCurrent;
                return true;
            }

            if (checkpoints.TryGetCheckpoint(nextIndex, out uint checkpointIndex, out uint checkpointSector)
                && checkpointIndex == nextIndex)
            {
                index = nextIndex;
                current = new(checkpointSector, Context.SectorSize);
                return true;
            }

            uint nextSectorId = ReadNextDifatSectorId(current);
            index = nextIndex;
            current = new(nextSectorId, Context.SectorSize);
            return true;
        }

        return TryAdvanceFrontier();
    }

    private bool TryInitializeFrontier()
    {
        if (hasFrontier)
            return true;

        if (endReached)
            return false;

        uint firstSectorId = Context.Header.FirstDifatSectorId;
        if (firstSectorId == SectorType.EndOfChain)
        {
            endReached = true;
            index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        if (Context.Header.DifatSectorCount == 0)
            throw new FileFormatException("DIFAT chain index is greater than the sector count.");
        if (Context.SectorCount == 0)
            throw new FileFormatException("DIFAT chain index is greater than the maximum for the stream length.");

        hasFrontier = true;
        frontierIndex = 0;
        frontierCurrent = new(firstSectorId, Context.SectorSize);
        checkpoints.Add(0, firstSectorId);
        return true;
    }

    private uint ReadNextDifatSectorId(Sector sector)
    {
        CfbBinaryReader reader = Context.Reader;
        uint nextSectorId;
        try
        {
            reader.Position = sector.EndPosition - sizeof(uint);
            nextSectorId = reader.ReadUInt32();
        }
        catch (EndOfStreamException exception)
        {
            throw new FileFormatException("DIFAT sector is truncated.", exception);
        }

        if (nextSectorId != SectorType.EndOfChain)
            ThrowHelper.ThrowIfSectorIdIsInvalid(nextSectorId);

        return nextSectorId;
    }

    private bool TryAdvanceFrontier()
    {
        if (endReached)
        {
            index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        uint nextSectorId = ReadNextDifatSectorId(frontierCurrent);
        if (nextSectorId == SectorType.EndOfChain)
        {
            endReached = true;
            index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        uint nextIndex = frontierIndex + 1;
        if (nextIndex >= Context.Header.DifatSectorCount)
            throw new FileFormatException("DIFAT chain index is greater than the sector count.");
        if (nextIndex >= Context.SectorCount)
            throw new FileFormatException("DIFAT chain index is greater than the maximum for the stream length.");

        frontierIndex = nextIndex;
        frontierCurrent = new(nextSectorId, Context.SectorSize);
        checkpoints.Add(frontierIndex, nextSectorId);

        index = frontierIndex;
        current = frontierCurrent;
        return true;
    }

    public bool MoveTo(uint index)
    {
        if (index >= Context.Header.DifatSectorCount)
            return false;

        if (started && this.index == index)
            return true;

        started = true;
        if (!TryInitializeFrontier())
        {
            this.index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        if (index <= frontierIndex)
        {
            if (index == frontierIndex)
            {
                this.index = frontierIndex;
                current = frontierCurrent;
                return true;
            }

            bool hasCheckpoint = checkpoints.TryGetCheckpoint(index, out uint checkpointIndex, out uint checkpointSector);
            Debug.Assert(hasCheckpoint);

            if (this.index > index || this.index < checkpointIndex)
            {
                this.index = checkpointIndex;
                current = new(checkpointSector, Context.SectorSize);
            }

            while (this.index < index)
            {
                uint nextSectorId = ReadNextDifatSectorId(current);
                this.index++;
                current = new(nextSectorId, Context.SectorSize);
            }

            return true;
        }

        if (endReached)
        {
            this.index = uint.MaxValue;
            current = Sector.EndOfChain;
            return false;
        }

        this.index = frontierIndex;
        current = frontierCurrent;

        while (this.index < index)
        {
            if (!TryAdvanceFrontier())
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

        if (!hasFrontier)
        {
            hasFrontier = true;
            frontierIndex = 0;
            frontierCurrent = newDifatSector;
            checkpoints.Add(0, newDifatSector.Id);
        }
        else
        {
            frontierIndex = header.DifatSectorCount - 1;
            frontierCurrent = newDifatSector;
            checkpoints.Add(frontierIndex, newDifatSector.Id);
        }

        endReached = true;
        started = true;
        index = header.DifatSectorCount - 1;
        current = newDifatSector;
    }
}
