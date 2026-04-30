namespace OpenMcdf;

/// <summary>
/// Represents a stream in a compound file. Provides read, write, seek, and length operations for compound file streams.
/// </summary>
public sealed class CfbStream : Stream
{
    private readonly RootContextSite rootContextSite;
    private readonly DirectoryEntry directoryEntry;
    private Stream stream;
    private bool isDisposed;

    long MaxStreamLength => this.rootContextSite.Context.MaxStreamLength;

    internal CfbStream(RootContextSite rootContextSite, DirectoryEntry directoryEntry, Storage parent)
    {
        this.rootContextSite = rootContextSite;
        this.directoryEntry = directoryEntry;
        Parent = parent;
        stream = directoryEntry.StreamLength < Header.MiniStreamCutoffSize
            ? new MiniFatStream(rootContextSite, directoryEntry)
            : new FatStream(rootContextSite, directoryEntry);
    }

    protected override void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
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
            EntryInfo parentEntryInfo = Parent.EntryInfo;
            string path = $"{parentEntryInfo.Path}{parentEntryInfo.Name}";
            return directoryEntry.ToEntryInfo(path);
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => stream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => stream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => stream.CanWrite;

    /// <inheritdoc/>
    public override long Length => stream.Length;

    /// <inheritdoc/>
    public override long Position { get => stream.Position; set => stream.Position = value; }

    /// <inheritdoc/>
    public override void Flush()
    {
        this.ThrowIfDisposed(isDisposed);

        stream.Flush();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, offset, count);

        this.ThrowIfDisposed(isDisposed);

        return stream.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfDisposed(isDisposed);

        return stream.Seek(offset, origin);
    }

    private void EnsureLengthToWrite(int count)
    {
        long newPosition = Position + count;
        if (newPosition > MaxStreamLength)
            throw new IOException("Stream was too long");
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
        this.ThrowIfNotWritable();

        EnsureLengthToWrite(count);

        stream.Write(buffer, offset, count);
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);

        return stream.Read(buffer);
    }

    /// <inheritdoc/>
    public override int ReadByte() => this.ReadByteCore();

    /// <inheritdoc/>
    public override void WriteByte(byte value) => this.WriteByteCore(value);

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfNotWritable();

        EnsureLengthToWrite(buffer.Length);

        stream.Write(buffer);
    }

#endif
}
