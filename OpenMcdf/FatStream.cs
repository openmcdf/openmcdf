using System.Diagnostics;

namespace OpenMcdf;

/// <summary>
/// Provides a <inheritdoc cref="Stream"/> for a stream object in a compound file./>.
/// </summary>
internal sealed class FatStream : Stream
{
    readonly RootContextSite rootContextSite;
    readonly FatChainEnumerator chain;
    long position;
    bool isDirectoryEntryDirty;
    bool isDisposed;

    private RootContext Context => rootContextSite.Context;

    internal FatStream(RootContextSite rootContextSite, DirectoryEntry directoryEntry)
    {
        this.rootContextSite = rootContextSite;
        DirectoryEntry = directoryEntry;
        chain = new(Context.Fat, directoryEntry.StartSectorId);
    }

    internal DirectoryEntry DirectoryEntry { get; private set; }

    internal long ChainCapacity => ((Length + Context.SectorSize - 1) / Context.SectorSize) * Context.SectorSize;

    /// <inheritdoc/>
    public override bool CanRead => !isDisposed;

    /// <inheritdoc/>
    public override bool CanSeek => !isDisposed;

    /// <inheritdoc/>
    public override bool CanWrite => !isDisposed && Context.CanWrite;

    /// <inheritdoc/>
    public override long Length => DirectoryEntry.StreamLength;

    /// <inheritdoc/>
    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            try
            {
                Flush();
            }
            finally
            {
                chain.Dispose();
                isDisposed = true;
            }
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        this.ThrowIfDisposed(isDisposed);

        if (isDirectoryEntryDirty)
        {
            Context.DirectoryEntries.Write(DirectoryEntry);
            isDirectoryEntryDirty = false;
        }

        if (CanWrite)
            Context.Writer.Flush();
    }

    internal MiniFatStream SwitchToMiniFatStream(long length)
    {
        MiniFatStream? miniFatStream = null;

        long originalPosition = Position;

        try
        {
            DirectoryEntry newDirectoryEntry = DirectoryEntry.CloneWithNoStream();
            miniFatStream = new(rootContextSite, newDirectoryEntry);
            miniFatStream.SetLength(length);

            SetLength(length); // Truncate the stream

            Position = 0;
            CopyTo(miniFatStream);
            miniFatStream.Position = originalPosition;

            SetLength(0);
            isDirectoryEntryDirty = false; // Ownership of the directory entry is transferred to the mini FAT stream.

            return miniFatStream;
        }
        catch
        {
            try
            {
                miniFatStream?.SetLength(0);
            }
            finally
            {
                Position = originalPosition;
                miniFatStream?.Dispose();
            }

            throw;
        }
    }

    uint GetFatChainIndexAndSectorOffset(long offset, out long sectorOffset) => (uint)Math.DivRem(offset, Context.SectorSize, out sectorOffset);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed(isDisposed);

        if (count == 0)
            return 0;

        int maxCount = (int)Math.Min(Math.Max(Length - position, 0), int.MaxValue);
        if (maxCount == 0)
            return 0;

        uint chainIndex = GetFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!chain.MoveTo(chainIndex))
            throw new FileFormatException($"The FAT chain was shorter than the stream length.");

        int realCount = Math.Min(count, maxCount);
        int readCount = 0;
        while (true)
        {
            Sector sector = chain.CurrentSector;
            int remaining = realCount - readCount;
            long readLength = Math.Min(remaining, sector.Length - sectorOffset);
            Context.Reader.Position = sector.Position + sectorOffset;
            int localOffset = offset + readCount;
            int read = Context.Reader.Read(buffer, localOffset, (int)readLength);
            if (read == 0)
                return readCount;
            position += read;
            readCount += read;
            sectorOffset = 0;
            if (readCount >= realCount)
                return readCount;
            if (!chain.MoveNext())
                throw new FileFormatException($"The FAT chain was shorter than the stream length.");
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfDisposed(isDisposed);

        switch (origin)
        {
            case SeekOrigin.Begin:
                if (offset < 0)
                    ThrowHelper.ThrowSeekBeforeOrigin();
                position = offset;
                break;

            case SeekOrigin.Current:
                if (position + offset < 0)
                    ThrowHelper.ThrowSeekBeforeOrigin();
                position += offset;
                break;

            case SeekOrigin.End:
                if (Length + offset < 0)
                    ThrowHelper.ThrowSeekBeforeOrigin();
                position = Length + offset;
                break;

            default:
                throw new ArgumentException("Invalid seek origin", nameof(origin));
        }

        return position;
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        this.ThrowIfDisposed(isDisposed);

        uint requiredChainLength = (uint)((value + Context.SectorSize - 1) / Context.SectorSize);
        if (value > ChainCapacity)
            DirectoryEntry.StartSectorId = chain.Extend(requiredChainLength);
        else if (value <= ChainCapacity - Context.SectorSize)
            DirectoryEntry.StartSectorId = chain.Shrink(requiredChainLength);

        DirectoryEntry.StreamLength = value;
        isDirectoryEntryDirty = true;
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed(isDisposed);

        if (count == 0)
            return;

        uint chainIndex = GetFatChainIndexAndSectorOffset(position, out long sectorOffset);

        CfbBinaryWriter writer = Context.Writer;
        int writeCount = 0;
        uint lastIndex = 0;
        do
        {
            if (!chain.MoveTo(chainIndex))
                lastIndex = chain.ExtendFrom(lastIndex);

            Sector sector = chain.CurrentSector;
            writer.Position = sector.Position + sectorOffset;
            int remaining = count - writeCount;
            int localOffset = offset + writeCount;
            long writeLength = Math.Min(remaining, sector.Length - sectorOffset);
            writer.Write(buffer, localOffset, (int)writeLength);
            Context.ExtendStreamLength(sector.EndPosition);
            Debug.Assert(Context.Length >= Context.Stream.Length);
            position += writeLength;
            writeCount += (int)writeLength;
            sectorOffset = 0;
            chainIndex++;
        } while (writeCount < count);

        if (position > Length)
        {
            DirectoryEntry.StreamLength = position;
            isDirectoryEntryDirty = true;
        }
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK

    public override int ReadByte() => this.ReadByteCore();

    public override int Read(Span<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);

        if (buffer.Length == 0)
            return 0;

        int maxCount = (int)Math.Min(Math.Max(Length - position, 0), int.MaxValue);
        if (maxCount == 0)
            return 0;

        uint chainIndex = GetFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!chain.MoveTo(chainIndex))
            throw new FileFormatException($"The FAT chain was shorter than the stream length.");

        int realCount = Math.Min(buffer.Length, maxCount);
        int readCount = 0;
        while (true)
        {
            Sector sector = chain.CurrentSector;
            int remaining = realCount - readCount;
            long readLength = Math.Min(remaining, sector.Length - sectorOffset);
            Context.Reader.Position = sector.Position + sectorOffset;
            int localOffset = readCount;
            Span<byte> slice = buffer.Slice(localOffset, (int)readLength);
            int read = Context.Reader.Read(slice);
            if (read == 0)
                return readCount;
            position += read;
            readCount += read;
            sectorOffset = 0;
            if (readCount >= realCount)
                return readCount;
            if (!chain.MoveNext())
                throw new FileFormatException($"The FAT chain was shorter than the stream length.");
        }
    }

    public override void WriteByte(byte value) => this.WriteByteCore(value);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);

        if (buffer.Length == 0)
            return;

        uint chainIndex = GetFatChainIndexAndSectorOffset(position, out long sectorOffset);

        CfbBinaryWriter writer = Context.Writer;
        int writeCount = 0;
        uint lastIndex = 0;
        do
        {
            if (!chain.MoveTo(chainIndex))
                lastIndex = chain.ExtendFrom(lastIndex);

            Sector sector = chain.CurrentSector;
            writer.Position = sector.Position + sectorOffset;
            int remaining = buffer.Length - writeCount;
            int localOffset = writeCount;
            long writeLength = Math.Min(remaining, sector.Length - sectorOffset);
            ReadOnlySpan<byte> slice = buffer.Slice(localOffset, (int)writeLength);
            writer.Write(slice);
            Context.ExtendStreamLength(sector.EndPosition);
            position += writeLength;
            writeCount += (int)writeLength;
            sectorOffset = 0;
            chainIndex++;
        } while (writeCount < buffer.Length);

        if (position > Length)
        {
            DirectoryEntry.StreamLength = position;
            isDirectoryEntryDirty = true;
        }
    }

#endif
}
