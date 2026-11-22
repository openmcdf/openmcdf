using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="MiniSector"/>s in a Mini FAT chain.
/// </summary>
internal sealed class MiniFatChainEnumerator : ContextBase, IEnumerator<uint>
{
    private readonly MiniFatEnumerator miniFatEnumerator;
    private ChainCheckpointTable checkpoints;
    private uint startId;
    private bool started = false;
    uint index = uint.MaxValue;
    private uint current = uint.MaxValue;
    private long length = -1;

    // Discovery frontier and Brent's cycle-finding algorithm
    private bool hasFrontier = false;
    private bool endReached = false;
    private uint frontierIndex = uint.MaxValue;
    private uint frontierCurrent = uint.MaxValue;
    private uint cycleLength = 1;
    private uint power = 1;
    private uint slow = uint.MaxValue;

    public MiniFatChainEnumerator(RootContextSite rootContextSite, uint startSectorId)
        : base(rootContextSite)
    {
        startId = startSectorId;
        miniFatEnumerator = new(rootContextSite);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        checkpoints.Clear();
        miniFatEnumerator.Dispose();
    }

    /// <summary>
    /// Gets the current <see cref="MiniSector"/> in the Mini FAT chain, or throws if the enumeration has not started.
    /// </summary>
    public MiniSector CurrentSector => new(Current, Context.MiniSectorSize);

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

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (!started)
        {
            started = true;
            if (!TryInitializeFrontier())
            {
                index = uint.MaxValue;
                current = uint.MaxValue;
                return false;
            }

            index = 0;
            current = startId;
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

            current = Context.MiniFat[current];
            index = nextIndex;
            return true;
        }

        return TryAdvanceFrontier();
    }

    private bool TryInitializeFrontier()
    {
        if (hasFrontier)
            return true;

        if (SectorType.IsFreeOrEndOfChain(startId))
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

        uint value = Context.MiniFat[frontierCurrent];
        if (value == SectorType.EndOfChain)
        {
            endReached = true;
            length = (long)frontierIndex + 1;
            index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        if (value > SectorType.Maximum)
            throw new FileFormatException($"Invalid mini FAT sector ID: {value}.");

        uint nextIndex = frontierIndex + 1;
        if (nextIndex > SectorType.Maximum)
            throw new FileFormatException("Mini FAT chain length is greater than the maximum.");

        if (value == slow && slow != uint.MaxValue)
            throw new FileFormatException("Mini FAT chain contains a loop.");

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
            // Each step records the next sector, until the end of the chain
        }
    }

    /// <summary>
    /// Moves to the specified index within the mini FAT sector chain.
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
                current = Context.MiniFat[current];
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

    public uint Extend(uint requiredChainLength)
    {
        uint chainLength = (uint)GetLength();
        if (chainLength >= requiredChainLength)
            throw new ArgumentException("The chain is already longer than required.", nameof(requiredChainLength));

        // Empty chains start with either NoStream or EndOfChain, which the reference implementation writes
        if (SectorType.IsFreeOrEndOfChain(startId))
        {
            startId = Context.MiniFat.Add(miniFatEnumerator, 0);
            RecordAppendedSector(startId);
            chainLength = 1;
        }

        bool ok = MoveTo(chainLength - 1);
        Debug.Assert(ok);

        uint lastId = current;
        ok = miniFatEnumerator.MoveTo(lastId);
        Debug.Assert(ok);
        while (chainLength < requiredChainLength)
        {
            uint id = Context.MiniFat.Add(miniFatEnumerator, lastId);
            Context.MiniFat[lastId] = id;
            RecordAppendedSector(id);
            lastId = id;
            chainLength++;
        }

#if DEBUG
        using (MiniFatChainEnumerator verifyEnumerator = new(ContextSite, startId))
        {
            Debug.Assert(verifyEnumerator.GetLength() == requiredChainLength);
        }
#endif

        length = requiredChainLength;
        return startId;
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
                if (lastId <= SectorType.Maximum)
                {
                    if (index == requiredChainLength)
                        Context.MiniFat[lastId] = SectorType.EndOfChain;
                    else if (index > requiredChainLength)
                        Context.MiniFat[lastId] = SectorType.Free;
                }

                lastId = current;
            }

            if (lastId <= SectorType.Maximum)
                Context.MiniFat[lastId] = SectorType.Free;

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

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Index: {index} Current: {current}";
}
