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
    private uint startId;
    private bool started = false;
    uint index = uint.MaxValue;
    private uint current = uint.MaxValue;
    private long length = -1;

    // Brent's cycle-finding algorithm
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
            index = 0;
            current = startId;
        }
        else if (!SectorType.IsFreeOrEndOfChain(current))
        {
            uint value = Context.MiniFat[current];
            if (value == SectorType.EndOfChain)
            {
                index = uint.MaxValue;
                current = uint.MaxValue;
                slow = uint.MaxValue;
                return false;
            }

            if (value > SectorType.Maximum)
                throw new FileFormatException($"Invalid mini FAT sector ID: {value}.");

            ValidateMiniSectorId(value);

            uint nextIndex = index + 1;
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

            index = nextIndex;
            current = value;
            cycleLength++;
            return true;
        }

        if (SectorType.IsFreeOrEndOfChain(current))
        {
            index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        if (current > SectorType.Maximum)
            throw new FileFormatException($"Invalid mini FAT sector ID: {current}.");

        ValidateMiniSectorId(current);

        return true;
    }

    /// <summary>
    /// Moves to the specified index within the mini FAT sector chain.
    /// </summary>
    /// <param name="index">The index to move to.</param>
    /// <returns>true if the enumerator was successfully advanced to the given index.</returns>
    public bool MoveTo(uint index)
    {
        if (index < this.index)
            Reset();

        while (!started || this.index < index)
        {
            if (!MoveNext())
                return false;
        }

        return true;
    }

    public long GetLength()
    {
        if (length == -1)
        {
            Reset();
            length = 0;
            while (MoveNext())
            {
                length++;
            }
        }

        return length;
    }

    public uint Extend(uint requiredChainLength)
    {
        uint chainLength = (uint)GetLength();
        if (chainLength >= requiredChainLength)
            throw new ArgumentException("The chain is already longer than required.", nameof(requiredChainLength));

        if (startId == StreamId.NoStream)
        {
            startId = Context.MiniFat.Add(miniFatEnumerator, 0);
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
            lastId = id;
            chainLength++;
        }

#if DEBUG
        this.length = -1;
        this.length = GetLength();
        Debug.Assert(length == requiredChainLength);
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

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        index = uint.MaxValue;
        current = uint.MaxValue;
        slow = uint.MaxValue;
        cycleLength = 1;
        power = 1;
    }

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Index: {index} Current: {current}";

    void ValidateMiniSectorId(uint sectorId)
    {
        ulong maxMiniFatEntries = (ulong)Context.Header.MiniFatSectorCount * (ulong)(Context.SectorSize / sizeof(uint));
        if ((ulong)sectorId >= maxMiniFatEntries)
            throw new FileFormatException($"Mini FAT sector ID {sectorId} is beyond the Mini FAT entry count.");
    }
}
