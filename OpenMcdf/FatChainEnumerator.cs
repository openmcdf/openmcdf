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
    private uint startId;
    private bool started = false;
    private uint index = uint.MaxValue;
    private uint current = uint.MaxValue;
    private long length = -1;

    // Brent's cycle detection algorithm
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
            if (startId is SectorType.EndOfChain or SectorType.Free)
            {
                index = uint.MaxValue;
                current = uint.MaxValue;
                return false;
            }

            index = 0;
            current = startId;
            started = true;
            slow = uint.MaxValue;
            return true;
        }

        if (index == uint.MaxValue)
            return false;

        uint value = Fat[current];
        if (value is SectorType.EndOfChain)
        {
            index = uint.MaxValue;
            current = uint.MaxValue;
            return false;
        }

        if (value > SectorType.Maximum)
            throw new FileFormatException($"Invalid FAT sector ID: {value}.");

        index++;
        if (index >= Context.SectorCount)
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

        current = value;
        cycleLength++;
        return true;
    }

    /// <summary>
    /// Moves to the specified index within the FAT sector chain.
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

        if (startId == StreamId.NoStream)
        {
            startId = Fat.Add(fatEnumerator, 0);
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
            lastId = id;
            chainLength++;
        }

        length = requiredChainLength;
        return startId;
    }

    public uint ExtendFrom(uint hintId)
    {
        if (startId == SectorType.EndOfChain)
        {
            startId = Fat.Add(fatEnumerator, hintId);
            return startId;
        }

        Reset();

        uint lastId = startId;
        while (MoveNext())
        {
            lastId = current;
        }

        uint id = Fat.Add(fatEnumerator, hintId);
        Fat[lastId] = id;
        return id;
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
    public void Reset() => Reset(startId);

    public void Reset(uint startSectorId)
    {
        startId = startSectorId;
        started = false;
        index = uint.MaxValue;
        current = uint.MaxValue;
        slow = uint.MaxValue;
        cycleLength = 1;
        power = 1;
    }

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Index: {index} Current: {current}";
}
