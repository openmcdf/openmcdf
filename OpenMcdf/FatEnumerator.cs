using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Enumerates the <see cref="FatEntry"/> records in a <see cref="Fat"/>.
/// </summary>
internal sealed class FatEnumerator : ContextBase, IEnumerator<FatEntry>
{
    bool started = false;
    uint index = uint.MaxValue;
    uint value = uint.MaxValue;

    public FatEnumerator(RootContextSite rootContextSite)
        : base(rootContextSite)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    public FatEntry Current
    {
        get
        {
            ThrowHelper.ThrowIfEnumerationNotStarted(started);
            return new(index, value);
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
            return MoveTo(0);
        }

        if (index >= SectorType.Maximum)
            return false;

        uint next = index + 1;
        return MoveTo(next);
    }

    public bool MoveTo(uint index)
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(index);

        started = true;
        if (this.index == index)
            return true;

        if (Context.Fat.TryGetValue(index, out value))
        {
            if (value < SectorType.Maximum && value >= Context.SectorCount)
                throw new FileFormatException($"FAT entry #{index} for sector {value} is beyond the end of the stream.");
            this.index = index;
            return true;
        }

        this.index = uint.MaxValue;
        return false;
    }

    public bool MoveNextFreeEntry()
    {
        while (MoveNext())
        {
            if (value is SectorType.Free)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        index = uint.MaxValue;
        value = uint.MaxValue;
    }

    [ExcludeFromCodeCoverage]
    public override string ToString() => $"{Current}";
}
