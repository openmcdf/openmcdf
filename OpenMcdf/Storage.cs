using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// An object in a compound file that is analogous to a file system directory. Provides methods to create, open, enumerate, and delete entries, as well as to manage metadata.
/// </summary>
public class Storage : ContextBase
{
    readonly DirectoryTree directoryTree;
    readonly DirectoryEntry directoryEntry;
    readonly string path;

    public Storage? Parent { get; }

    internal Storage(RootContextSite rootContextSite, DirectoryEntry directoryEntry, Storage? parent)
        : base(rootContextSite)
    {
        if (directoryEntry.Type is not StorageType.Storage and not StorageType.Root)
            throw new ArgumentException("DirectoryEntry must be a Storage or Root.", nameof(directoryEntry));

        directoryTree = new(Context.DirectoryEntries, directoryEntry);
        this.directoryEntry = directoryEntry;
        Parent = parent;
        path = parent is null ? $"/" : $"{parent.path}{parent.EntryInfo.Name}/";
    }

    /// <summary>
    /// Gets metadata about this storage entry.
    /// </summary>
    public EntryInfo EntryInfo => directoryEntry.ToEntryInfo(path);

    [Obsolete("Use CLSID instead.")]
    public Guid CLISD
    {
        get => CLSID;
        set => CLSID = value;
    }

    public Guid CLSID
    {
        get => directoryEntry.CLSID;
        set
        {
            Context.ThrowIfNotWritable();

            directoryEntry.CLSID = value;
            Context.DirectoryEntries.Write(directoryEntry);
        }
    }

    /// <summary>
    /// Gets or sets the creation time for this storage.
    /// </summary>
    public DateTime CreationTime
    {
        get => directoryEntry.CreationTime;
        set
        {
            Context.ThrowIfNotWritable();

            if (directoryEntry.Type is StorageType.Root && value != FileTime.UtcZero)
                throw new ArgumentException("Creation time must be zero for the root storage.", nameof(value));

            directoryEntry.CreationTime = value;
            Context.DirectoryEntries.Write(directoryEntry);
        }
    }

    /// <summary>
    /// Gets or sets the last modified time for this storage.
    /// </summary>
    public DateTime ModifiedTime
    {
        get => directoryEntry.ModifiedTime;
        set
        {
            Context.ThrowIfNotWritable();

            directoryEntry.ModifiedTime = value;
            Context.DirectoryEntries.Write(directoryEntry);
        }
    }

    /// <summary>
    /// Gets or sets the state bits for this storage.
    /// </summary>
    public uint StateBits
    {
        get => directoryEntry.StateBits;
        set
        {
            Context.ThrowIfNotWritable();

            directoryEntry.StateBits = value;
            Context.DirectoryEntries.Write(directoryEntry);
        }
    }

    /// <summary>
    /// Enumerates all entries (storages and streams) contained in this storage.
    /// </summary>
    /// <returns>An enumerable of <see cref="EntryInfo"/> objects.</returns>
    public IEnumerable<EntryInfo> EnumerateEntries()
    {
        this.ThrowIfDisposed(Context.IsDisposed);

        EntryInfo entryInfo = EntryInfo;
        string path = $"{entryInfo.Path}{entryInfo.Name}";
        return EnumerateDirectoryEntries()
            .Select(e => e.ToEntryInfo(path));
    }

    internal IEnumerable<DirectoryEntry> EnumerateDirectoryEntries()
    {
        using DirectoryTreeEnumerator treeEnumerator = new(Context.DirectoryEntries, directoryEntry);
        while (treeEnumerator.MoveNext())
        {
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

        directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry);
        if (entry is null || entry.Type is not StorageType.Storage)
        {
            storage = null;
            return false;
        }

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

        if (destination == this)
            throw new ArgumentException("A storage cannot be copied to itself.", nameof(destination));

        foreach (DirectoryEntry entry in EnumerateDirectoryEntries())
        {
            Copy(entry, destination);
        }
    }

    private void Copy(DirectoryEntry entry, Storage destination)
    {
        if (entry.Type is StorageType.Storage)
        {
            Storage subSource = new(ContextSite, entry, this);
            Storage subDestination = destination.CreateStorage(entry.NameString);
            subSource.CopyTo(subDestination);
        }
        else if (entry.Type is StorageType.Stream)
        {
            using CfbStream stream = new(ContextSite, entry, this);
            using CfbStream destinationStream = destination.CreateStream(entry.NameString);
            stream.CopyTo(destinationStream);
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
        Context.ThrowIfNotWritable();

        directoryTree.TryGetDirectoryEntry(name, out DirectoryEntry? entry);
        if (entry is null)
            return;

        DeleteEntry(entry);
    }

    void DeleteEntry(DirectoryEntry entry)
    {
        if (entry.Type is StorageType.Storage && entry.ChildId is not StreamId.NoStream)
        {
            Storage storage = new(ContextSite, entry, this);
            while (storage.TryDeleteFirstChildEntry())
            {
            }
        }
        else if (entry.Type is StorageType.Stream && !SectorType.IsFreeOrEndOfChain(entry.StartSectorId))
        {
            if (entry.StreamLength < Header.MiniStreamCutoffSize)
            {
                using MiniFatChainEnumerator miniFatChainEnumerator = new(ContextSite, entry.StartSectorId);
                miniFatChainEnumerator.Truncate();
            }
            else
            {
                using FatChainEnumerator fatChainEnumerator = new(Context.Fat, entry.StartSectorId);
                fatChainEnumerator.Truncate();
            }
        }

        directoryTree.Remove(entry);
    }

    bool TryDeleteFirstChildEntry()
    {
        bool found = Context.DirectoryEntries.TryGetDictionaryEntry(directoryEntry.ChildId, true, out DirectoryEntry? childEntry);
        if (childEntry is not null)
            DeleteEntry(childEntry);
        return found;
    }

    [ExcludeFromCodeCoverage]
    internal void TraceDirectoryEntries(TextWriter writer) => directoryTree.WriteTrace(writer);
}
