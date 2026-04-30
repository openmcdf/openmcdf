using System.Collections;

namespace OpenMcdf;

/// <summary>
/// Enumerates <see cref="DirectoryEntry"/> instances from a <see cref="DirectoryEntries"/>.
/// </summary>
internal sealed class DirectoryEntryEnumerator : IEnumerator<DirectoryEntry>
{
    private readonly DirectoryEntries directories;
    private bool started = false;
    private uint index = uint.MaxValue;
    private DirectoryEntry? current;

    public DirectoryEntryEnumerator(DirectoryEntries directories)
    {
        this.directories = directories;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    public DirectoryEntry Current
    {
        get
        {
            ThrowHelper.ThrowIfEnumerationNotStarted(started);
            return current!;
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
        if (!directories.TryGetDictionaryEntry(nextIndex, false, out current))
        {
            index = uint.MaxValue;
            return false;
        }

        index = nextIndex;
        return true;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        started = false;
        current = null;
        index = uint.MaxValue;
    }
}
