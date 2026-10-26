namespace OpenMcdf;

/// <summary>
/// Represents a stream in a compound file. Provides read, write, seek, and length operations for compound file streams.
/// </summary>
public sealed class CfbStream : Stream
{
    private readonly RootContextSite rootContextSite;
    private readonly uint directoryEntryId;
    private readonly int generation;
    private readonly int entryDeletionCount;
    private Stream stream;
    private bool isDisposed;

    long MaxStreamLength => this.rootContextSite.Context.MaxStreamLength;

    // Consolidating or reverting rewrites the directory entries and sector chains, and deleting the stream frees its
    // sectors and recycles its entry, so the stream no longer refers to its data
    bool IsInvalidated => generation != rootContextSite.Generation
        || entryDeletionCount != rootContextSite.GetEntryDeletionCount(directoryEntryId);

    internal CfbStream(RootContextSite rootContextSite, DirectoryEntry directoryEntry, Storage parent)
    {
        this.rootContextSite = rootContextSite;
        directoryEntryId = directoryEntry.Id;
        generation = rootContextSite.Generation;
        entryDeletionCount = rootContextSite.GetEntryDeletionCount(directoryEntryId);
        Parent = parent;
        stream = directoryEntry.StreamLength < Header.MiniStreamCutoffSize
            ? new MiniFatStream(rootContextSite, directoryEntry)
            : new FatStream(rootContextSite, directoryEntry);
    }

    protected override void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            // Flushing an invalidated stream would overwrite another directory entry
            if (!IsInvalidated)
                stream.Dispose();
            isDisposed = true;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Gets the parent storage containing this stream.
    /// </summary>
    public Storage Parent { get; }

    /// <summary>
    /// Gets metadata about this stream entry.
    /// </summary>
    public EntryInfo EntryInfo
    {
        get
        {
            this.ThrowIfInvalidated(IsInvalidated);

            EntryInfo parentEntryInfo = Parent.EntryInfo;
            string path = $"{parentEntryInfo.Path}{parentEntryInfo.Name}";
            DirectoryEntry entry = rootContextSite.Context.DirectoryEntries.GetDirectoryEntry(directoryEntryId);
            entry.StreamLength = stream.Length; // The stream may not have been flushed yet
            return entry.ToEntryInfo(path);
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => !isDisposed && !IsInvalidated && stream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => !isDisposed && !IsInvalidated && stream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => !isDisposed && !IsInvalidated && stream.CanWrite;

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            this.ThrowIfInvalidated(IsInvalidated);

            return stream.Length;
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get
        {
            this.ThrowIfInvalidated(IsInvalidated);

            return stream.Position;
        }

        set
        {
            this.ThrowIfInvalidated(IsInvalidated);

            stream.Position = value;
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        stream.Flush();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, offset, count);

        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        return stream.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        return stream.Seek(offset, origin);
    }

    private void EnsureLengthToWrite(int count)
    {
        long newPosition = Position + count;
        if (newPosition > MaxStreamLength)
            throw new IOException("Stream was too long.");
        if (newPosition > stream.Length)
            SetLengthCore(newPosition);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value > MaxStreamLength)
            throw new ArgumentOutOfRangeException(nameof(value));

        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        this.ThrowIfNotWritable();

        SetLengthCore(value);
    }

    void SetLengthCore(long value)
    {
        if (value >= Header.MiniStreamCutoffSize && stream is MiniFatStream miniStream)
        {
            stream = miniStream.SwitchToFatStream(value);
            miniStream.Dispose();
        }
        else if (value < Header.MiniStreamCutoffSize && stream is FatStream fatStream)
        {
            stream = fatStream.SwitchToMiniFatStream(value);
            fatStream.Dispose();
        }
        else
        {
            stream.SetLength(value);
        }
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, offset, count);

        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        this.ThrowIfNotWritable();

        EnsureLengthToWrite(count);

        stream.Write(buffer, offset, count);
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        return stream.Read(buffer);
    }

    /// <inheritdoc/>
    public override int ReadByte() => this.ReadByteCore();

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        // Check before the helper, which would otherwise report the invalidated stream as not writable
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);

        this.WriteByteCore(value);
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfInvalidated(IsInvalidated);
        this.ThrowIfNotWritable();

        EnsureLengthToWrite(buffer.Length);

        stream.Write(buffer);
    }

#endif
}
