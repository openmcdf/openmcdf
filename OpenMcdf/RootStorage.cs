using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Represents the major version of the compound file.
/// </summary>
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Compound file versions are defined as ushort.")]
public enum Version : ushort
{
    /// <summary>
    /// Unknown version.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 512 byte sectors.
    /// </summary>
    V3 = 3,

    /// <summary>
    /// 4096 byte sectors.
    /// </summary>
    V4 = 4,
}

/// <summary>
/// Specifies options for configuring the behavior of a compound file root storage.
/// </summary>
[Flags]
public enum StorageModeFlags
{
    /// <summary>
    /// Default mode with no special flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Leaves the underlying stream open after the <see cref="RootStorage"/> is disposed.
    /// </summary>
    LeaveOpen = 0x01,

    /// <summary>
    /// Allows the compound file to be used in a transacted context, enabling rollback of changes.
    /// </summary>
    Transacted = 0x02,

    /// <summary>
    /// Enables strict validation of the compound file structure, throwing exceptions for any inconsistencies or errors.
    /// </summary>
    StrictValidation = 0x04,
}

/// <summary>
/// Encapsulates the root <see cref="Storage"/> of a compound file. Provides methods to create, open, commit, revert, and consolidate compound files.
/// </summary>
public sealed class RootStorage : Storage, IDisposable
{
    readonly StorageModeFlags storageModeFlags;

    private static void ThrowIfInvalid(FileMode mode)
    {
        if (mode is FileMode.Append)
            throw new ArgumentException("Append mode is not valid for compound files.", nameof(mode));
    }

    private static void ThrowIfInvalid(FileMode mode, FileAccess access)
    {
        if (mode is FileMode.Create or FileMode.CreateNew && access is FileAccess.Read)
            throw new ArgumentException($"{nameof(FileMode)} {mode} is not valid in conjunction with {nameof(FileAccess)} {access}.", nameof(mode));
    }

    private static void ThrowIfInvalid(FileAccess access, StorageModeFlags storageMode)
    {
        if (access is FileAccess.Write)
            throw new ArgumentException("Write-only access is not valid for compound files.", nameof(access));
        if (!access.HasFlag(FileAccess.ReadWrite))
            ThrowIfTransacted(storageMode);
    }

    private static void ThrowIfInvalid(Version version)
    {
        if (version is Version.Unknown)
            throw new ArgumentException("Cannot create compound files with an unknown version.", nameof(version));
    }

    private static void ThrowIfLeaveOpen(StorageModeFlags flags)
    {
        if (flags.HasFlag(StorageModeFlags.LeaveOpen))
            throw new ArgumentException($"{StorageModeFlags.LeaveOpen} is only valid for injected streams.");
    }

    private static void ThrowIfTransacted(StorageModeFlags flags)
    {
        if (flags.HasFlag(StorageModeFlags.Transacted))
            throw new ArgumentException($"{StorageModeFlags.Transacted} requires read-write access.", nameof(flags));
    }

    private static IOContextFlags ToIOContextFlags(StorageModeFlags flags)
    {
        IOContextFlags contextFlags = IOContextFlags.None;
        if (flags.HasFlag(StorageModeFlags.LeaveOpen))
            contextFlags |= IOContextFlags.LeaveOpen;
        if (flags.HasFlag(StorageModeFlags.Transacted))
            contextFlags |= IOContextFlags.Transacted;
        if (flags.HasFlag(StorageModeFlags.StrictValidation))
            contextFlags |= IOContextFlags.StrictValidation;
        return contextFlags;
    }

    /// <summary>
    /// Creates a new compound file at the specified file path.
    /// </summary>
    /// <param name="fileName">The file path to create the compound file.</param>
    /// <param name="version">The compound file version.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>A new <see cref="RootStorage"/> instance.</returns>
    public static RootStorage Create(string fileName, Version version = Version.V3, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (fileName is null)
            throw new ArgumentNullException(nameof(fileName));

        ThrowIfInvalid(version);
        ThrowIfLeaveOpen(flags);

        FileStream stream = File.Create(fileName);
        return Create(stream, version, flags);
    }

    /// <summary>
    /// Creates a new compound file in the specified stream.
    /// </summary>
    /// <param name="stream">The stream to use for the compound file.</param>
    /// <param name="version">The compound file version.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>A new <see cref="RootStorage"/> instance.</returns>
    public static RootStorage Create(Stream stream, Version version = Version.V3, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        stream.ThrowIfSeekingNotSupported();
        stream.ThrowIfWritingNotSupported();
        ThrowIfInvalid(version);

        stream.SetLength(0);
        stream.Position = 0;

        IOContextFlags contextFlags = ToIOContextFlags(flags) | IOContextFlags.Create;
        RootContextSite rootContextSite = new();
        _ = new RootContext(rootContextSite, stream, version, contextFlags);
        return new RootStorage(rootContextSite, flags);
    }

    /// <summary>
    /// Creates a new in-memory compound file.
    /// </summary>
    /// <param name="version">The compound file version.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>A new <see cref="RootStorage"/> instance.</returns>
    public static RootStorage CreateInMemory(Version version = Version.V3, StorageModeFlags flags = StorageModeFlags.None)
    {
        ThrowIfInvalid(version);
        ThrowIfLeaveOpen(flags);

        return Create(new MemoryStream(), version, flags);
    }

    /// <summary>
    /// Opens an existing compound file from the specified file path.
    /// </summary>
    /// <param name="fileName">The file path to open.</param>
    /// <param name="mode">The file mode to use.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>An opened <see cref="RootStorage"/> instance.</returns>
    public static RootStorage Open(string fileName, FileMode mode, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (fileName is null)
            throw new ArgumentNullException(nameof(fileName));

        ThrowIfInvalid(mode);
        ThrowIfLeaveOpen(flags);

        FileStream stream = File.Open(fileName, mode);
        return Open(stream, flags);
    }

    /// <summary>
    /// Opens an existing compound file from the specified file path with access control.
    /// </summary>
    /// <param name="fileName">The file path to open.</param>
    /// <param name="mode">The file mode to use.</param>
    /// <param name="access">The file access mode.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>An opened <see cref="RootStorage"/> instance.</returns>
    public static RootStorage Open(string fileName, FileMode mode, FileAccess access, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (fileName is null)
            throw new ArgumentNullException(nameof(fileName));

        ThrowIfInvalid(mode);
        ThrowIfInvalid(mode, access);
        ThrowIfInvalid(access, flags);
        ThrowIfLeaveOpen(flags);

        FileStream stream = File.Open(fileName, mode, access);
        return Open(stream, flags);
    }

    /// <summary>
    /// Opens an existing compound file from the specified stream.
    /// </summary>
    /// <param name="stream">The stream to open.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>An opened <see cref="RootStorage"/> instance.</returns>
    public static RootStorage Open(Stream stream, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        stream.ThrowIfSeekingNotSupported();
        if (!stream.CanWrite)
            ThrowIfTransacted(flags);
        stream.Position = 0;

        IOContextFlags contextFlags = ToIOContextFlags(flags);
        RootContextSite rootContextSite = new();
        _ = new RootContext(rootContextSite, stream, Version.Unknown, contextFlags);
        return new RootStorage(rootContextSite, flags);
    }

    /// <summary>
    /// Opens an existing compound file for read-only access.
    /// </summary>
    /// <param name="fileName">The file path to open.</param>
    /// <param name="flags">Flags controlling storage behavior.</param>
    /// <returns>An opened <see cref="RootStorage"/> instance.</returns>
    public static RootStorage OpenRead(string fileName, StorageModeFlags flags = StorageModeFlags.None)
    {
        if (fileName is null)
            throw new ArgumentNullException(nameof(fileName));

        ThrowIfLeaveOpen(flags);
        ThrowIfTransacted(flags);

        FileStream stream = File.OpenRead(fileName);
        return Open(stream, flags);
    }

    RootStorage(RootContextSite rootContextSite, StorageModeFlags storageModeFlags)
        : base(rootContextSite, rootContextSite.Context.DirectoryEntries.RootEntry, null)
    {
        this.storageModeFlags = storageModeFlags;
    }

    /// <summary>
    /// Disposes the current context of the compound file.
    /// </summary>
    public void Dispose() => Context.Dispose();

    /// <summary>
    /// Gets the underlying stream for this compound file.
    /// </summary>
    public Stream BaseStream => Context.BaseStream;

    /// <summary>
    /// Gets a value indicating whether the root storage supports writing.
    /// </summary>
    public bool CanWrite => !Context.IsDisposed && Context.CanWrite;

    /// <summary>
    /// Gets a value indicating whether the root storage supports committing changes.
    /// </summary>
    public bool CanCommit => !Context.IsDisposed && Context.CanCommit;

    /// <summary>
    /// Flushes changes to the underlying stream. Optionally consolidates the file.
    /// </summary>
    /// <param name="consolidate">If true, consolidates the file after flushing.</param>
    public void Flush(bool consolidate = false)
    {
        this.ThrowIfDisposed(Context.IsDisposed);

        Context.Flush();

        if (consolidate)
            Consolidate();
    }

    void Consolidate()
    {
        // TODO: Consolidate by defragmentation instead of copy
        Stream baseStream = Context.BaseStream;
        Stream? destinationStream = null;

        try
        {
            destinationStream = baseStream switch
            {
                MemoryStream => new MemoryStream((int)baseStream.Length),
                FileStream => File.Create(Path.GetTempFileName()),
                _ => throw new NotSupportedException("Unsupported stream type for consolidation."),
            };

            using (RootStorage destinationStorage = Create(destinationStream, Context.Version, StorageModeFlags.LeaveOpen))
                CopyTo(destinationStorage);

            destinationStream.CopyAllTo(baseStream);

            IOContextFlags contextFlags = ToIOContextFlags(storageModeFlags);
            _ = new RootContext(ContextSite, baseStream, Version.Unknown, contextFlags);
        }
        finally
        {
            destinationStream?.Dispose();

            if (destinationStream is FileStream fs)
            {
                string fileName = fs.Name;
                File.Delete(fileName);
            }
        }
    }

    /// <summary>
    /// Commits all changes to the compound file.
    /// </summary>
    public void Commit()
    {
        this.ThrowIfDisposed(Context.IsDisposed);

        Context.Commit();
    }

    /// <summary>
    /// Reverts all uncommitted changes to the compound file.
    /// </summary>
    public void Revert()
    {
        this.ThrowIfDisposed(Context.IsDisposed);

        Context.Revert();
    }

    private void SwitchToCore(Stream stream, bool allowLeaveOpen)
    {
        Flush();
        Context.Stream.CopyAllTo(stream);
        Context.Dispose();

        IOContextFlags contextFlags = ToIOContextFlags(storageModeFlags);
        if (!allowLeaveOpen)
            contextFlags &= ~IOContextFlags.LeaveOpen;
        _ = new RootContext(ContextSite, stream, Version.Unknown, contextFlags);
    }

    /// <summary>
    /// Switches the underlying storage to a new stream.
    /// </summary>
    /// <param name="stream">The new stream to use.</param>
    public void SwitchTo(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        ThrowHelper.ThrowIfSeekingNotSupported(stream);
        ThrowHelper.ThrowIfWritingNotSupported(stream);

        SwitchToCore(stream, true);
    }

    /// <summary>
    /// Switches the underlying storage to a new file.
    /// </summary>
    /// <param name="fileName">The new file to use.</param>
    public void SwitchTo(string fileName)
    {
        if (fileName is null)
            throw new ArgumentNullException(nameof(fileName));

        FileStream stream = File.Create(fileName);
        SwitchToCore(stream, false);
    }

    // TODO: Move checks to Tests project as Asserts
    [ExcludeFromCodeCoverage]
    internal bool Validate()
    {
        // Validate will throw on error, return a bool for test purposes
        Context.Validate();

#if NET10_0_OR_GREATER
        ValidateOrphans();
#endif

        return true;
    }

#if NET10_0_OR_GREATER
    [ExcludeFromCodeCoverage]
    IEnumerable<DirectoryEntry> EnumerateDirectoryEntriesRecursively()
    {
        this.ThrowIfDisposed(Context.IsDisposed);

        var stack = new Stack<Storage>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            Storage storage = stack.Pop();

            foreach (DirectoryEntry entry in storage.EnumerateDirectoryEntries())
            {
                yield return entry;

                if (entry.EntryType is EntryType.Storage)
                {
                    Storage childStorage = storage.OpenStorage(entry.NameString);
                    stack.Push(childStorage);
                }
            }
        }
    }

    [ExcludeFromCodeCoverage]
    Dictionary<StorageType, int> GetRecursiveStorageTypeCounts() => EnumerateDirectoryEntriesRecursively()
        .CountBy(e => e.Type)
        .ToDictionary();

    [ExcludeFromCodeCoverage]
    void ValidateOrphans()
    {
        Dictionary<StorageType, int> directoryEntryCounts = Context.DirectoryEntries.GetStorageTypeCounts();
        Dictionary<StorageType, int> entryInfoCounts = GetRecursiveStorageTypeCounts();

        if (directoryEntryCounts.GetValueOrDefault(StorageType.Stream) != entryInfoCounts.GetValueOrDefault(StorageType.Stream))
            throw new FileFormatException("Orphaned streams.");
        if (directoryEntryCounts.GetValueOrDefault(StorageType.Storage) != entryInfoCounts.GetValueOrDefault(StorageType.Storage))
            throw new FileFormatException("Orphaned storages.");
    }
#endif

    [ExcludeFromCodeCoverage]
    internal void WriteTrace(TextWriter writer) => Context.WriteTrace(writer);
}
