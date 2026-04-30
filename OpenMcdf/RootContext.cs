using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

[Flags]
enum IOContextFlags
{
    None = 0,
    Create = 1,
    LeaveOpen = 2,
    Transacted = 4,
}

/// <summary>
/// Encapsulates the objects required to read and write data to and from a compound file.
/// </summary>
internal sealed class RootContext : ContextBase, IDisposable
{
    // V3 compound files are limited to 2 GB
    internal const long MaximumV3StreamLength = 2147483648;
    internal const uint RangeLockSectorOffset = 0x7FFFFF00;
    internal const uint RangeLockSectorId = RangeLockSectorOffset / (1 << Header.SectorShiftV4) - 1;

    readonly IOContextFlags contextFlags;
    readonly CfbBinaryWriter? writer;
    readonly TransactedStream? transactedStream;
    MiniFat? miniFat;
    FatStream? miniStream;

    public Header Header { get; }

    public Stream BaseStream { get; }

    public Stream Stream { get; }

    public CfbBinaryReader Reader { get; }

    public CfbBinaryWriter Writer => writer switch
    {
        null => throw new InvalidOperationException("Stream is not writable"),
        _ => writer,
    };

    public Fat Fat { get; }

    public DirectoryEntries DirectoryEntries { get; }

    public MiniFat MiniFat
    {
        get
        {
            miniFat ??= new(ContextSite);
            return miniFat;
        }
    }

    public FatStream MiniStream
    {
        get
        {
            miniStream ??= new(ContextSite, DirectoryEntries.RootEntry);
            return miniStream;
        }
    }

    public bool CanWrite => writer is not null;

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the size of a regular sector.
    /// </summary>
    public int SectorSize { get; }

    public int MiniSectorSize { get; }

    public int FatEntriesPerSector { get; }

    public int DifatEntriesPerSector { get; }

    public int DirectoryEntriesPerSector { get; }

    public long MaxStreamLength { get; }

    public Version Version => (Version)Header.MajorVersion;

    public long Length { get; private set; }

    public uint SectorCount => (uint)Math.Max(0, (Length - SectorSize) / SectorSize); // TODO: Check

    public RootContext(RootContextSite rootContextSite, Stream stream, Version version, IOContextFlags contextFlags = IOContextFlags.None)
        : base(rootContextSite)
    {
        rootContextSite.Switch(this);

        BaseStream = stream;
        this.contextFlags = contextFlags;

        bool create = contextFlags.HasFlag(IOContextFlags.Create);

        using CfbBinaryReader reader = new(stream);
        Header = create ? new(version) : reader.ReadHeader();
        SectorSize = 1 << Header.SectorShift;
        MiniSectorSize = 1 << Header.MiniSectorShift;
        FatEntriesPerSector = SectorSize / sizeof(uint);
        DifatEntriesPerSector = FatEntriesPerSector - 1;
        DirectoryEntriesPerSector = SectorSize / DirectoryEntry.Length;
        MaxStreamLength = Version is Version.V3 ? MaximumV3StreamLength : SectorType.Maximum * SectorSize;

        Length = stream.Length;

        if (contextFlags.HasFlag(IOContextFlags.Transacted))
        {
            Stream overlayStream = stream is MemoryStream ? new MemoryStream() : File.Create(Path.GetTempFileName());
            transactedStream = new TransactedStream(ContextSite, stream, overlayStream);
            Stream = new BufferedStream(transactedStream, SectorSize);
        }
        else
        {
            Stream = stream;
        }

        Reader = new(Stream);
        if (stream.CanWrite)
            writer = new(Stream);

        Fat = new(ContextSite);
        DirectoryEntries = new(ContextSite, create);

        if (create)
        {
            WriteHeader();
            DirectoryEntries.Write(DirectoryEntries.RootEntry);
        }
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            Flush();

            miniStream?.Dispose();
            miniFat?.Dispose();
            DirectoryEntries.Dispose();
            Fat.Dispose();
            writer?.Dispose();
            Reader.Dispose();
            string? overlayFileName = (transactedStream?.OverlayStream as FileStream)?.Name;
            transactedStream?.Dispose();
            if (overlayFileName is not null)
                File.Delete(overlayFileName);
            if (!contextFlags.HasFlag(IOContextFlags.LeaveOpen))
                BaseStream.Dispose();
            IsDisposed = true;
        }
    }

    [MemberNotNull(nameof(writer))]
    public void ThrowIfNotWritable()
    {
        if (writer is null)
            throw new NotSupportedException("Root storage is not writable.");
    }

    [MemberNotNull(nameof(transactedStream))]
    public void ThrowIfNotTransacted()
    {
        if (transactedStream is null)
            throw new NotSupportedException("Cannot commit non-transacted storage.");
    }

    public void Flush()
    {
        if (writer is null)
            return;

        miniStream?.Flush();
        miniFat?.Flush();
        Fat.Flush();
        if (transactedStream is null)
            TrimBaseStream();
        WriteHeader();
        writer.BaseStream.Flush();
    }

    public void ExtendStreamLength(long length)
    {
        if (Length >= length)
            return;

        if (Version is Version.V3 && length > MaximumV3StreamLength)
            throw new IOException("V3 compound files are limited to 2 GB.");
        else if (Version is Version.V4 && Length < RangeLockSectorOffset && length >= RangeLockSectorOffset)
            Fat[RangeLockSectorId] = SectorType.EndOfChain;
        Length = length;
    }

    void TrimBaseStream()
    {
        Sector lastUsedSector = Fat.GetLastUsedSector();
        if (lastUsedSector.Id > SectorType.Maximum)
            throw new FileFormatException("Last used sector is invalid");

        if (Version is Version.V4 && lastUsedSector.EndPosition < RangeLockSectorOffset)
            Fat.TrySetValue(RangeLockSectorId, SectorType.Free);

        Length = lastUsedSector.EndPosition;
        BaseStream.SetLength(Length);
    }

    public void WriteHeader()
    {
        CfbBinaryWriter writer = Writer;
        writer.Seek(0, SeekOrigin.Begin);
        writer.Write(Header);
    }

    public void Commit()
    {
        ThrowIfNotWritable();
        ThrowIfNotTransacted();

        Flush();

        transactedStream.Commit();
    }

    public void Revert()
    {
        ThrowIfNotTransacted();

        transactedStream.Revert();
    }

    [ExcludeFromCodeCoverage]
    public void Validate()
    {
        Fat.Validate();
        MiniFat.Validate();
        DirectoryEntries.Validate();
    }

    [ExcludeFromCodeCoverage]
    public void WriteTrace(TextWriter writer)
    {
        writer.WriteLine(Header);
        writer.WriteLine();

        Fat.WriteTrace(writer);
        writer.WriteLine();

        MiniFat.WriteTrace(writer);
        writer.WriteLine();

        DirectoryEntries.WriteTrace(writer);
        writer.WriteLine();
    }
}
