using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

internal enum SiblingType
{
    Left,
    Right,
}

/// <summary>
/// Encapsulates getting and adding <see cref="DirectoryEntry"/> objects.
/// </summary>
internal sealed class DirectoryEntries : ContextBase, IDisposable
{
    private readonly FatChainEnumerator fatChainEnumerator;
    private readonly DirectoryEntryEnumerator directoryEntryEnumerator;

    public DirectoryEntry RootEntry { get; }

    public DirectoryEntries(RootContextSite rootContextSite, bool create)
        : base(rootContextSite)
    {
        fatChainEnumerator = new FatChainEnumerator(Context.Fat, Context.Header.FirstDirectorySectorId);
        directoryEntryEnumerator = new DirectoryEntryEnumerator(this);

        if (create)
        {
            RootEntry = CreateOrRecycleDirectoryEntry();
            RootEntry.RecycleRoot();
        }
        else
        {
            RootEntry = GetDictionaryEntry(0);
        }
    }

    public void Dispose()
    {
        directoryEntryEnumerator.Dispose();
        fatChainEnumerator.Dispose();
    }

    /// <summary>
    /// Gets the <see cref="DirectoryEntry"/> for the specified stream ID.
    /// </summary>
    public DirectoryEntry GetDictionaryEntry(uint streamId)
    {
        ThrowHelper.ThrowIfDirectoryEntryStreamIdIsInvalid(streamId);

        DirectoryEntry? entry = TryGetDictionaryEntryCore(streamId);
        if (entry is null)
            ThrowHelper.ThrowDirectoryEntryNotFound(streamId);
        return entry;
    }

    public bool TryGetDictionaryEntry(uint streamId, bool throwIfNotFound, [MaybeNullWhen(false)] out DirectoryEntry entry)
    {
        if (streamId == StreamId.NoStream)
        {
            entry = null;
            return false;
        }

        ThrowHelper.ThrowIfDirectoryEntryStreamIdIsInvalid(streamId);

        entry = TryGetDictionaryEntryCore(streamId);
        if (entry is null && throwIfNotFound)
            ThrowHelper.ThrowDirectoryEntryNotFound(streamId);
        return entry is not null;
    }

    DirectoryEntry? TryGetDictionaryEntryCore(uint streamId)
    {
        uint chainIndex = GetChainIndexAndEntryIndex(streamId, out long entryIndex);
        if (!fatChainEnumerator.MoveTo(chainIndex))
        {
            return null;
        }

        CfbBinaryReader reader = Context.Reader;
        reader.Position = fatChainEnumerator.CurrentSector.Position + (entryIndex * DirectoryEntry.Length);
        return reader.ReadDirectoryEntry(Context.Version, streamId);
    }

    public DirectoryEntry? TryGetSibling(DirectoryEntry entry, SiblingType siblingType, IDirectoryTreeValidator validator)
    {
        uint siblingId = entry.GetSiblingId(siblingType);
        if (!TryGetDictionaryEntry(siblingId, true, out DirectoryEntry? sibling))
            return null;

        validator.Validate(entry, sibling, siblingType);
        return sibling;
    }

    public DirectoryEntry GetSibling(DirectoryEntry entry, SiblingType siblingType, IDirectoryTreeValidator validator)
    {
        uint siblingId = entry.GetSiblingId(siblingType);
        DirectoryEntry sibling = GetDictionaryEntry(siblingId);
        validator.Validate(entry, sibling, siblingType);
        return sibling;
    }

    private uint GetChainIndexAndEntryIndex(uint streamId, out long entryIndex) => (uint)Math.DivRem(streamId, Context.DirectoryEntriesPerSector, out entryIndex);

    public DirectoryEntry CreateOrRecycleDirectoryEntry()
    {
        DirectoryEntry? entry = TryRecycleDirectoryEntry();
        if (entry is not null)
            return entry;

        CfbBinaryWriter writer = Context.Writer;
        uint id = fatChainEnumerator.Extend();
        Header header = Context.Header;
        if (header.FirstDirectorySectorId == SectorType.EndOfChain)
            header.FirstDirectorySectorId = id;
        if (Context.Version == Version.V4)
            header.DirectorySectorCount++;

        Sector sector = new(id, Context.SectorSize);
        writer.Position = sector.Position;
        for (int i = 0; i < Context.DirectoryEntriesPerSector; i++)
            writer.Write(DirectoryEntry.Unallocated);

        entry = TryRecycleDirectoryEntry()
            ?? throw new InvalidOperationException("Failed to add or recycle directory entry.");
        return entry;
    }

    private DirectoryEntry? TryRecycleDirectoryEntry()
    {
        directoryEntryEnumerator.Reset();

        while (directoryEntryEnumerator.MoveNext())
        {
            DirectoryEntry current = directoryEntryEnumerator.Current;
            if (current.Type is StorageType.Unallocated)
                return current;
        }

        return null;
    }

    public void Write(DirectoryEntry entry)
    {
        uint chainIndex = GetChainIndexAndEntryIndex(entry.Id, out long entryIndex);
        if (!fatChainEnumerator.MoveTo(chainIndex))
            throw new FileFormatException($"Directory entry {entry.Id} was not found.");

        CfbBinaryWriter writer = Context.Writer;
        writer.Position = fatChainEnumerator.CurrentSector.Position + (entryIndex * DirectoryEntry.Length);
        writer.Write(entry);
    }

    [ExcludeFromCodeCoverage]
    public IEnumerable<DirectoryEntry> Enumerate()
    {
        using DirectoryEntryEnumerator enumerator = new(this);
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }

#if NET10_0_OR_GREATER
    [ExcludeFromCodeCoverage]
    public Dictionary<StorageType, int> GetStorageTypeCounts() => Enumerate()
        .CountBy(e => e.Type)
        .ToDictionary();
#endif

    [ExcludeFromCodeCoverage]
    public void Validate()
    {
        DirectoryTree tree = new(this, RootEntry);
        tree.Validate();
    }

    [ExcludeFromCodeCoverage]
    public void WriteTrace(TextWriter writer)
    {
        DirectoryTree tree = new(this, RootEntry);
        tree.WriteTrace(writer);
    }
}
