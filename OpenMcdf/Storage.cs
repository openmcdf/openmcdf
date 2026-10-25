using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// An object in a compound file that is analogous to a file system directory. Provides methods to create, open, enumerate, and delete entries, as well as to manage metadata.
/// </summary>
public class Storage : ContextBase
{
    readonly string path;
    readonly DirectoryTree directoryTree;
    readonly uint directoryEntryId;
    readonly int entryDeletionCount;
    int generation;

    public Storage? Parent { get; }

    internal Storage(RootContextSite rootContextSite, DirectoryEntry directoryEntry, Storage? parent)
        : base(rootContextSite)
    {
        if (directoryEntry.Type is not StorageType.Storage and not StorageType.Root)
            throw new ArgumentException("DirectoryEntry must be a Storage or Root.", nameof(directoryEntry));

        directoryTree = new(rootContextSite, directoryEntry.Id);
        directoryEntryId = directoryEntry.Id;
        generation = rootContextSite.Generation;
        entryDeletionCount = rootContextSite.GetEntryDeletionCount(directoryEntryId);
        Parent = parent;
        path = parent is null ? $"/" : $"{parent.path}{parent.EntryInfo.Name}/";
    }

    // Consolidation rewrites the directory entries, and deleting the storage recycles its entry,
    // so the ID may refer to a different entry
    bool IsInvalidated => generation != ContextSite.Generation
        || entryDeletionCount != ContextSite.GetEntryDeletionCount(directoryEntryId);

    // The root entry ID is unchanged by consolidation, so the root storage remains valid
    private protected void Revalidate() => generation = ContextSite.Generation;

    // Read the entry on each use, since adding or removing siblings and children changes it
    DirectoryEntry GetDirectoryEntry()
    {
        this.ThrowIfInvalidated(IsInvalidated);

        return Context.DirectoryEntries.GetDictionaryEntry(directoryEntryId);
    }

    void UpdateDirectoryEntry(Action<DirectoryEntry> update)
    {
        Context.ThrowIfNotWritable();

        DirectoryEntry entry = GetDirectoryEntry();
        update(entry);
        Context.DirectoryEntries.Write(entry);
    }

    // Creating an entry resets its metadata, so copies keep the metadata of the source
    void CopyMetadataFrom(DirectoryEntry source) => UpdateDirectoryEntry(entry =>
    {
        entry.CLSID = source.CLSID;
        entry.StateBits = source.StateBits;
        if (entry.Type is not StorageType.Root)
            entry.CreationTime = source.CreationTime; // Must be zero for the root storage
        entry.ModifiedTime = source.ModifiedTime;
    });

    private protected void CopyMetadataTo(Storage destination) => destination.CopyMetadataFrom(GetDirectoryEntry());

    /// <summary>
    /// Gets metadata about this storage entry.
    /// </summary>
    public EntryInfo EntryInfo => GetDirectoryEntry().ToEntryInfo(path);

    [Obsolete("Use CLSID instead.")]
    public Guid CLISD
    {
        get => CLSID;
        set => CLSID = value;
    }

    public Guid CLSID
    {
        get => GetDirectoryEntry().CLSID;
        set => UpdateDirectoryEntry(entry => entry.CLSID = value);
    }

    /// <summary>
    /// Gets or sets the creation time for this storage.
    /// </summary>
    public DateTime CreationTime
    {
        get => FileTime.ClampToDateTimeUtc(CreationFileTime);
        set
        {
            Context.ThrowIfNotWritable();
            CreationFileTime = (ulong)value.ToFileTimeUtc();
        }
    }

    public ulong CreationFileTime
    {
        get => GetDirectoryEntry().CreationTime;
        set => UpdateDirectoryEntry(entry =>
        {
            if (entry.Type is StorageType.Root && value != 0)
                throw new ArgumentException("Creation time must be zero for the root storage.", nameof(value));
            entry.CreationTime = value;
        });
    }

    /// <summary>
    /// Gets or sets the last modified time for this storage.
    /// </summary>
    public DateTime ModifiedTime
    {
        get => FileTime.ClampToDateTimeUtc(ModifiedFileTime);
        set
        {
            Context.ThrowIfNotWritable();
            ModifiedFileTime = (ulong)value.ToFileTimeUtc();
        }
    }

    public ulong ModifiedFileTime
    {
        get => GetDirectoryEntry().ModifiedTime;
        set => UpdateDirectoryEntry(entry => entry.ModifiedTime = value);
    }

    /// <summary>
    /// Gets or sets the state bits for this storage.
    /// </summary>
    public uint StateBits
    {
        get => GetDirectoryEntry().StateBits;
        set => UpdateDirectoryEntry(entry => entry.StateBits = value);
    }

    /// <summary>
    /// Enumerates all entries (storages and streams) contained in this storage.
    /// </summary>
    /// <returns>An enumerable of <see cref="EntryInfo"/> objects.</returns>
    public IEnumerable<EntryInfo> EnumerateEntries()
    {
        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        EntryInfo entryInfo = EntryInfo;
        string path = $"{entryInfo.Path}{entryInfo.Name}";
        return EnumerateDirectoryEntries()
            .Select(e => e.ToEntryInfo(path));
    }

    internal IEnumerable<DirectoryEntry> EnumerateDirectoryEntries()
    {
        // The sequence is lazy, so check on each advance in case it is enumerated across a consolidation or deletion.
        // Compare with the generation at the start, since the root storage is revalidated by consolidation.
        this.ThrowIfInvalidated(IsInvalidated);
        int startGeneration = generation;

        using DirectoryTreeEnumerator treeEnumerator = new(ContextSite, directoryEntryId);
        while (true)
        {
            this.ThrowIfInvalidated(IsInvalidated || startGeneration != ContextSite.Generation);

            if (!treeEnumerator.MoveNext())
                yield break;

            yield return treeEnumerator.Current;
        }
    }

    /// <summary>
    /// Determines whether an entry with the specified name exists in this storage.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <returns>True if the entry exists; otherwise, false.</returns>
    public bool ContainsEntry(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        return directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? _);
    }

    /// <summary>
    /// Attempts to get metadata for an entry by name.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <param name="entryInfo">The entry info if found.</param>
    /// <returns>True if found; otherwise, false.</returns>
    public bool TryGetEntryInfo(string name, out EntryInfo entryInfo)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        if (!directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry))
        {
            entryInfo = default;
            return false;
        }

        string path = $"{EntryInfo.Path}{EntryInfo.Name}";
        entryInfo = entry.ToEntryInfo(path);
        return true;
    }

    DirectoryEntry AddDirectoryEntry(StorageType storageType, string name)
    {
        DirectoryEntry entry = Context.DirectoryEntries.CreateOrRecycleDirectoryEntry();
        entry.Recycle(storageType, name);
        directoryTree.Add(entry);
        return entry;
    }

    /// <summary>
    /// Creates a new storage entry with the specified name.
    /// </summary>
    /// <param name="name">The name of the new storage.</param>
    /// <returns>The created <see cref="Storage"/>.</returns>
    public Storage CreateStorage(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        Context.ThrowIfNotWritable();

        DirectoryEntry entry = AddDirectoryEntry(StorageType.Storage, name);
        return new Storage(ContextSite, entry, this);
    }

    /// <summary>
    /// Creates a new stream entry with the specified name.
    /// </summary>
    /// <param name="name">The name of the new stream.</param>
    /// <returns>The created <see cref="CfbStream"/>.</returns>
    public CfbStream CreateStream(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        Context.ThrowIfNotWritable();

        DirectoryEntry entry = AddDirectoryEntry(StorageType.Stream, name);
        return new CfbStream(ContextSite, entry, this);
    }

    /// <summary>
    /// Opens an existing storage entry by name.
    /// </summary>
    /// <param name="name">The name of the storage to open.</param>
    /// <returns>The opened <see cref="Storage"/>.</returns>
    public Storage OpenStorage(string name)
        => TryOpenStorage(name, out Storage? storage)
            ? storage
            : throw new DirectoryNotFoundException($"Storage not found: {name}.");

    /// <summary>
    /// Attempts to open a storage entry by name.
    /// </summary>
    /// <param name="name">The name of the storage.</param>
    /// <param name="storage">The opened storage if found.</param>
    /// <returns>True if found; otherwise, false.</returns>
    public bool TryOpenStorage(string name, [MaybeNullWhen(false)] out Storage storage)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry);
        if (entry is null || entry.Type is not StorageType.Storage)
        {
            storage = null;
            return false;
        }

        // Callers often open storages recursively, so reject a corrupt storage nested within itself
        for (Storage? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
            ThrowHelper.ThrowIfStorageIsRepeated(ancestor.directoryEntryId == entry.Id, entry);

        storage = new Storage(ContextSite, entry, this);
        return true;
    }

    /// <summary>
    /// Opens an existing stream entry by name.
    /// </summary>
    /// <param name="name">The name of the stream to open.</param>
    /// <returns>The opened <see cref="CfbStream"/>.</returns>
    public CfbStream OpenStream(string name)
         => TryOpenStream(name, out CfbStream? stream)
            ? stream
            : throw new FileNotFoundException($"Stream not found: {name}.", name);

    /// <summary>
    /// Attempts to open a stream entry by name.
    /// </summary>
    /// <param name="name">The name of the stream.</param>
    /// <param name="stream">The opened stream if found.</param>
    /// <returns>True if found; otherwise, false.</returns>
    public bool TryOpenStream(string name, [MaybeNullWhen(false)] out CfbStream stream)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry);
        if (entry is null || entry.Type is not StorageType.Stream)
        {
            stream = null;
            return false;
        }

        stream = new CfbStream(ContextSite, entry, this);
        return true;
    }

    /// <summary>
    /// Copies all entries from this storage to the specified destination storage.
    /// </summary>
    /// <param name="destination">The destination storage.</param>
    public void CopyTo(Storage destination)
    {
        if (destination is null)
            throw new ArgumentNullException(nameof(destination));

        this.ThrowIfInvalidated(IsInvalidated);
        destination.ThrowIfInvalidated(destination.IsInvalidated);

        // Copying into a descendant would keep copying the entries it creates
        for (Storage? ancestor = destination; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.ContextSite == ContextSite && ancestor.directoryEntryId == directoryEntryId)
                throw new ArgumentException("A storage cannot be copied to itself or one of its descendants.", nameof(destination));
        }

        // Copy without recursion, since storages can be nested deeply. Each storage can only be visited once,
        // which rejects corrupt files where a storage is nested within itself or shared.
        HashSet<uint> visited = [directoryEntryId];

        // The parent check above only follows the opened storages, so a corrupt file can still share the destination
        // with the source. Reaching it would copy the entries being created, so treat it as visited too.
        if (destination.ContextSite == ContextSite)
            visited.Add(destination.directoryEntryId);

        Stack<(Storage Source, Storage Destination)> storages = new();
        storages.Push((this, destination));
        while (storages.Count > 0)
        {
            (Storage source, Storage target) = storages.Pop();
            foreach (DirectoryEntry entry in source.EnumerateDirectoryEntries())
            {
                if (entry.Type is StorageType.Storage)
                {
                    ThrowHelper.ThrowIfStorageIsRepeated(!visited.Add(entry.Id), entry);
                    Storage subDestination = target.CreateStorage(entry.NameString);
                    subDestination.CopyMetadataFrom(entry);
                    storages.Push((new Storage(ContextSite, entry, source), subDestination));
                }
                else if (entry.Type is StorageType.Stream)
                {
                    using CfbStream stream = new(ContextSite, entry, source);
                    using CfbStream destinationStream = target.CreateStream(entry.NameString);
                    stream.CopyTo(destinationStream);
                }
            }
        }
    }

    /// <summary>
    /// Deletes the entry with the specified name from this storage.
    /// </summary>
    /// <remarks>
    /// If the file to be deleted does not exist, no exception is thrown.
    /// </remarks>
    /// <param name="name">The name of the entry to delete.</param>
    public void Delete(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ThrowHelper.ThrowIfNameIsInvalid(name);

        this.ThrowIfDisposed(Context.IsDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        Context.ThrowIfNotWritable();

        directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry);
        if (entry is null)
            return;

        DeleteEntry(entry);
    }

    void DeleteEntry(DirectoryEntry entry)
    {
        if (entry.Type is StorageType.Storage && entry.ChildId is not StreamId.NoStream)
            new Storage(ContextSite, entry, this).DeleteChildEntries();

        RemoveEntry(entry);
    }

    // Deletes depth first without recursion, since storages can be nested deeply
    void DeleteChildEntries()
    {
        // Each storage can only be visited once, which rejects corrupt files where a storage is nested within itself
        HashSet<uint> visited = [directoryEntryId];
        Stack<Storage> storages = new();
        storages.Push(this);
        while (storages.Count > 0)
        {
            Storage storage = storages.Peek();
            if (!Context.DirectoryEntries.TryGetDictionaryEntry(storage.GetDirectoryEntry().ChildId, true, out DirectoryEntry? child))
            {
                storages.Pop();
            }
            else if (child.Type is StorageType.Storage && child.ChildId is not StreamId.NoStream)
            {
                ThrowHelper.ThrowIfStorageIsRepeated(!visited.Add(child.Id), child);
                storages.Push(new Storage(ContextSite, child, storage));
            }
            else
            {
                storage.RemoveEntry(child);
            }
        }
    }

    // Frees the stream data of an entry and removes it from this storage, once any children are deleted
    void RemoveEntry(DirectoryEntry entry)
    {
        if (entry.Type is StorageType.Stream && !SectorType.IsFreeOrEndOfChain(entry.StartSectorId))
        {
            if (entry.StreamLength < Header.MiniStreamCutoffSize)
            {
                using MiniFatChainEnumerator miniFatChainEnumerator = new(ContextSite, entry.StartSectorId);
                miniFatChainEnumerator.Truncate();
            }
            else
            {
                using FatChainEnumerator fatChainEnumerator = new(ContextSite, entry.StartSectorId);
                fatChainEnumerator.Truncate();
            }
        }

        directoryTree.Remove(entry);
        ContextSite.InvalidateEntry(entry.Id);
    }

    [ExcludeFromCodeCoverage]
    internal void TraceDirectoryEntries(TextWriter writer) => directoryTree.WriteTrace(writer);
}
