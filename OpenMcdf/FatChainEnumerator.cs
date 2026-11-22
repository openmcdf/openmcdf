using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="Sector"/>s in a FAT sector chain.
/// </summary>
internal sealed class FatChainEnumerator : ContextBase, IEnumerator<uint>
{
    private readonly FatEnumerator fatEnumerator;
    private ChainCheckpointTable checkpoints;
    private uint startId;
    private bool started = false;
    private uint index = uint.MaxValue;
    private uint current = uint.MaxValue;
    private long length = -1;

    // Discovery frontier and Brent's cycle detection algorithm
    private bool hasFrontier = false;
    private bool endReached = false;
    private uint frontierIndex = uint.MaxValue;
    private uint frontierCurrent = uint.MaxValue;
    private uint cycleLength = 1;
    private uint power = 1;
    private uint slow = uint.MaxValue;

    public FatChainEnumerator(RootContextSite rootContextSite, uint startSectorId)
        : base(rootContextSite)
    {
        startId = startSectorId;
        fatEnumerator = new(rootContextSite);
    }

    // Resolve the FAT through the site so the chain follows the context when the root storage switches streams
    private Fat Fat => Context.Fat;

    /// <inheritdoc/>
    public void Dispose()
    {
        checkpoints.Clear();
        fatEnumerator.Dispose();
    }

    public Sector CurrentSector => new(current, Context.SectorSize);

    /// <inheritdoc/>
    public uint Current
    {
        get
        {
            ThrowHelper.ThrowIfEnumerationNotStarted(started);
            return current;
        }
    }

    /// <inheritdoc/>
    object IEnumerator.Current => Current;

    public bool IsAt(uint index) => started && index == this.index;

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (!started)
        {
            if (!TryInitializeFrontier())
            {
                index = uint.MaxValue;
                current = uint.MaxValue;
                return false;
            }

            index = 0;
            current = startId;
            started = true;
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
                current = checkpointSector;
                return true;
            }

            current = Fat[current];
            index = nextIndex;
            return true;
        }

        return TryAdvanceFrontier();
    }

    private bool TryInitializeFrontier()
    {
        if (hasFrontier)
            return true;

        if (startId is SectorType.EndOfChain or SectorType.Free)
        {
            endReached = true;
            length = 0;
            return false;
        }

        hasFrontier = true;
        frontierIndex = 0;
        frontierCurrent = startId;
        checkpoints.Add(0, startId);
        return true;
    }

    private bool TryAdvanceFrontier()
    {
        if (endReached)
        {
            index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        uint value = Fat[frontierCurrent];
        if (value is SectorType.EndOfChain)
        {
            endReached = true;
            length = (long)frontierIndex + 1;
            index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        if (value > SectorType.Maximum)
            throw new FileFormatException($"Invalid FAT sector ID: {value}.");

        uint nextIndex = frontierIndex + 1;
        if (nextIndex >= Context.SectorCount)
        {
            index = uint.MaxValue;
            current = uint.MaxValue;
            throw new FileFormatException("FAT chain index is greater than the sector count.");
        }

        if (value == slow && slow != uint.MaxValue)
            throw new FileFormatException("FAT chain contains a loop.");

        if (cycleLength == power)
        {
            cycleLength = 0;
            power *= 2;
            slow = value;
        }

        frontierIndex = nextIndex;
        frontierCurrent = value;
        cycleLength++;
        checkpoints.Add(frontierIndex, frontierCurrent);

        index = frontierIndex;
        current = frontierCurrent;
        return true;
    }

    private void RecordAppendedSector(uint sectorId)
    {
        if (!hasFrontier)
        {
            hasFrontier = true;
            endReached = true;
            frontierIndex = 0;
            frontierCurrent = sectorId;
            checkpoints.Add(0, sectorId);
            length = 1;
            return;
        }

        if (cycleLength == power)
        {
            cycleLength = 0;
            power *= 2;
            slow = sectorId;
        }

        frontierIndex++;
        frontierCurrent = sectorId;
        cycleLength++;
        checkpoints.Add(frontierIndex, sectorId);
        endReached = true;
        length = (long)frontierIndex + 1;
    }

    private void EnsureEndReached()
    {
        if (endReached)
            return;

        if (!TryInitializeFrontier())
            return;

        started = true;
        index = frontierIndex;
        current = frontierCurrent;
        while (TryAdvanceFrontier())
        {
        }
    }

    /// <summary>
    /// Moves to the specified index within the FAT sector chain.
    /// </summary>
    /// <param name="index">The index to move to.</param>
    /// <returns>true if the enumerator was successfully advanced to the given index.</returns>
    public bool MoveTo(uint index)
    {
        if (started && this.index == index)
            return true;

        if (!TryInitializeFrontier())
        {
            this.index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        if (index <= frontierIndex)
        {
            if (index == frontierIndex)
            {
                started = true;
                this.index = frontierIndex;
                current = frontierCurrent;
                return true;
            }

            bool hasCheckpoint = checkpoints.TryGetCheckpoint(index, out uint checkpointIndex, out uint checkpointSector);
            Debug.Assert(hasCheckpoint);

            if (!started || this.index > index || this.index < checkpointIndex)
            {
                started = true;
                this.index = checkpointIndex;
                current = checkpointSector;
            }

            while (this.index < index)
            {
                current = Fat[current];
                this.index++;
            }

            return true;
        }

        if (endReached)
        {
            started = true;
            this.index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        started = true;
        this.index = frontierIndex;
        current = frontierCurrent;

        while (this.index < index)
        {
            if (!TryAdvanceFrontier())
                return false;
        }

        return true;
    }

    public long GetLength()
    {
        if (length == -1)
            EnsureEndReached();

        return length;
    }

    /// <summary>
    /// Extends the chain by one.
    /// </summary>
    /// <returns>The ID of the new sector.</returns>
    public uint Extend() => ExtendFrom(0);

    /// <summary>
    /// Returns the ID of the first sector in the chain.
    /// </summary>
    public uint Extend(uint requiredChainLength)
    {
        uint chainLength = (uint)GetLength();
        if (chainLength >= requiredChainLength)
            throw new ArgumentException("The chain is already longer than required.", nameof(requiredChainLength));

        // Empty chains start with either NoStream or EndOfChain, which the reference implementation writes
        if (SectorType.IsFreeOrEndOfChain(startId))
        {
            startId = Fat.Add(fatEnumerator, 0);
            RecordAppendedSector(startId);
            chainLength = 1;
        }

        bool ok = MoveTo(chainLength - 1);
        Debug.Assert(ok);

        uint lastId = current;
        ok = fatEnumerator.MoveTo(lastId);
        Debug.Assert(ok);
        while (chainLength < requiredChainLength)
        {
            uint id = Fat.Add(fatEnumerator, lastId);
            Fat[lastId] = id;
            RecordAppendedSector(id);
            lastId = id;
            chainLength++;
        }

        return startId;
    }

    public uint ExtendFrom(uint hintId)
    {
        if (SectorType.IsFreeOrEndOfChain(startId))
        {
            startId = Fat.Add(fatEnumerator, hintId);
            RecordAppendedSector(startId);
            return startId;
        }

        EnsureEndReached();
        uint lastId = frontierCurrent;

        uint id = Fat.Add(fatEnumerator, hintId);
        Fat[lastId] = id;
        RecordAppendedSector(id);
        return id;
    }

    public uint Shrink(uint requiredChainLength)
    {
        uint chainLength = (uint)GetLength();
        if (chainLength <= requiredChainLength)
            throw new ArgumentException("The chain is already shorter than required.", nameof(requiredChainLength));

        Reset();

        try
        {
            uint lastId = current;
            while (MoveNext())
            {
                if (!SectorType.IsFreeOrEndOfChain(lastId))
                {
                    if (index == requiredChainLength)
                        Fat[lastId] = SectorType.EndOfChain;
                    else if (index > requiredChainLength)
                        Fat[lastId] = SectorType.Free;
                }

                lastId = current;
            }

            Fat[lastId] = SectorType.Free;

            if (requiredChainLength == 0)
            {
                startId = StreamId.NoStream;
            }
        }
        finally
        {
            InvalidateCache();
        }

#if DEBUG
        this.length = -1;
        this.length = GetLength();
        Debug.Assert(length == requiredChainLength);
#endif

        length = requiredChainLength;
        return startId;
    }

    public void Truncate()
    {
        uint chainLength = (uint)GetLength();
        if (chainLength > 0)
            Shrink(0);
    }

    private void InvalidateCache()
    {
        Reset();
        checkpoints.Clear();
        hasFrontier = false;
        endReached = false;
        frontierIndex = uint.MaxValue;
        frontierCurrent = uint.MaxValue;
        slow = uint.MaxValue;
        cycleLength = 1;
        power = 1;
        length = -1;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        index = uint.MaxValue;
        current = uint.MaxValue;
    }

    public void Reset(uint startSectorId)
    {
        if (startId != startSectorId)
        {
            startId = startSectorId;
            InvalidateCache();
        }
        else
        {
            Reset();
        }
    }

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Index: {index} Current: {current}";
}
