namespace OpenMcdf;

/// <summary>
/// Provides a <see cref="Stream"/> for reading a <see cref="DirectoryEntry"/> from the mini FAT stream.
/// </summary>
internal sealed class MiniFatStream : Stream
{
    readonly RootContextSite rootContextSite;
    readonly MiniFatChainEnumerator miniChain;
    long position;
    bool isDisposed;
    bool isDirectoryEntryDirty;

    internal MiniFatStream(RootContextSite rootContextSite, DirectoryEntry directoryEntry)
    {
        this.rootContextSite = rootContextSite;
        DirectoryEntry = directoryEntry;
        miniChain = new(rootContextSite, directoryEntry.StartSectorId);
    }

    RootContext Context => rootContextSite.Context;

    internal DirectoryEntry DirectoryEntry { get; private set; }

    internal long ChainCapacity => ((Length + Context.MiniSectorSize - 1) / Context.MiniSectorSize) * Context.MiniSectorSize;

    public override bool CanRead => !isDisposed;

    public override bool CanSeek => !isDisposed;

    public override bool CanWrite => !isDisposed && Context.CanWrite;

    public override long Length => DirectoryEntry.StreamLength;

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

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
                miniChain.Dispose();
                isDisposed = true;
            }
        }

        base.Dispose(disposing);
    }

    public override void Flush()
    {
        this.ThrowIfDisposed(isDisposed);

        if (isDirectoryEntryDirty)
        {
            Context.DirectoryEntries.Write(DirectoryEntry);
            isDirectoryEntryDirty = false;
        }

        Context.MiniStream.Flush();
    }

    internal FatStream SwitchToFatStream(long length)
    {
        long originalPosition = Position;

        FatStream? fatStream = null;

        try
        {
            DirectoryEntry newDirectoryEntry = DirectoryEntry.CloneWithNoStream();
            fatStream = new(rootContextSite, newDirectoryEntry);
            fatStream.SetLength(length);

            Position = 0;
            CopyTo(fatStream);
            fatStream.Position = originalPosition;

            SetLength(0);
            isDirectoryEntryDirty = false; // Ownership of the directory entry is transferred to the new fat stream

            return fatStream;
        }
        catch
        {
            try
            {
                fatStream?.SetLength(0);
            }
            finally
            {
                Position = originalPosition;
                fatStream?.Dispose();
            }

            throw;
        }
    }

    uint GetMiniFatChainIndexAndSectorOffset(long offset, out long sectorOffset) => (uint)Math.DivRem(offset, Context.MiniSectorSize, out sectorOffset);

    public override int Read(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed(isDisposed);

        if (count == 0)
            return 0;

        int maxCount = (int)Math.Min(Math.Max(Length - position, 0), int.MaxValue);
        if (maxCount == 0)
            return 0;

        uint chainIndex = GetMiniFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!miniChain.MoveTo(chainIndex))
            throw new FileFormatException($"The mini FAT chain was shorter than the stream length.");

        FatStream miniStream = Context.MiniStream;
        int realCount = Math.Min(count, maxCount);
        int readCount = 0;
        while (true)
        {
            MiniSector miniSector = miniChain.CurrentSector;
            int remaining = realCount - readCount;
            long readLength = Math.Min(remaining, miniSector.Length - sectorOffset);
            miniStream.Position = miniSector.Position + sectorOffset;
            int localOffset = offset + readCount;
            int read = miniStream.Read(buffer, localOffset, (int)readLength);
            if (read == 0)
                return readCount;
            position += read;
            readCount += read;
            sectorOffset = 0;
            if (readCount >= realCount)
                return readCount;
            if (!miniChain.MoveNext())
                throw new FileFormatException($"The mini FAT chain was shorter than the stream length.");
        }
    }

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
                throw new ArgumentException("Invalid seek origin.", nameof(origin));
        }

        return position;
    }

    public override void SetLength(long value)
    {
        if (value >= Header.MiniStreamCutoffSize)
            throw new ArgumentOutOfRangeException(nameof(value));

        this.ThrowIfDisposed(isDisposed);

        uint requiredChainLength = (uint)((value + Context.MiniSectorSize - 1) / Context.MiniSectorSize);
        if (value > ChainCapacity)
            DirectoryEntry.StartSectorId = miniChain.Extend(requiredChainLength);
        else if (value <= ChainCapacity - Context.MiniSectorSize)
            DirectoryEntry.StartSectorId = miniChain.Shrink(requiredChainLength);

        DirectoryEntry.StreamLength = value;
        isDirectoryEntryDirty = true;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        this.ThrowIfDisposed(isDisposed);
        this.ThrowIfNotWritable();

        if (count == 0)
            return;

        if (position + count > ChainCapacity)
            SetLength(position + count);

        uint chainIndex = GetMiniFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!miniChain.MoveTo(chainIndex))
            throw new FileFormatException($"Failed to move to mini FAT chain index: {chainIndex}.");

        FatStream miniStream = Context.MiniStream;
        int writeCount = 0;
        do
        {
            MiniSector miniSector = miniChain.CurrentSector;
            long basePosition = miniSector.Position + sectorOffset;
            miniStream.Seek(basePosition, SeekOrigin.Begin);
            int remaining = count - writeCount;
            int localOffset = offset + writeCount;
            long writeLength = Math.Min(remaining, Context.MiniSectorSize - sectorOffset);
            miniStream.Write(buffer, localOffset, (int)writeLength);
            position += writeLength;
            writeCount += (int)writeLength;
            if (position > Length)
            {
                DirectoryEntry.StreamLength = position;
                isDirectoryEntryDirty = true;
            }
            sectorOffset = 0;
            if (writeCount >= count)
                return;
        } while (miniChain.MoveNext());

        // Should be impossible since the chain length was validated
        throw new InvalidOperationException($"The end of mini FAT chain was reached.");
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

        uint chainIndex = GetMiniFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!miniChain.MoveTo(chainIndex))
            throw new FileFormatException($"The mini FAT chain was shorter than the stream length.");

        FatStream miniStream = Context.MiniStream;
        int realCount = Math.Min(buffer.Length, maxCount);
        int readCount = 0;
        while (true)
        {
            MiniSector miniSector = miniChain.CurrentSector;
            int remaining = realCount - readCount;
            long readLength = Math.Min(remaining, miniSector.Length - sectorOffset);
            miniStream.Position = miniSector.Position + sectorOffset;
            int localOffset = readCount;
            Span<byte> slice = buffer.Slice(localOffset, (int)readLength);
            int read = miniStream.Read(slice);
            if (read == 0)
                return readCount;
            position += read;
            readCount += read;
            sectorOffset = 0;
            if (readCount >= realCount)
                return readCount;
            if (!miniChain.MoveNext())
                throw new FileFormatException($"The mini FAT chain was shorter than the stream length.");
        }
    }

    public override void WriteByte(byte value) => this.WriteByteCore(value);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfDisposed(isDisposed);

        if (buffer.Length == 0)
            return;

        if (position + buffer.Length > ChainCapacity)
            SetLength(position + buffer.Length);

        uint chainIndex = GetMiniFatChainIndexAndSectorOffset(position, out long sectorOffset);
        if (!miniChain.MoveTo(chainIndex))
            throw new FileFormatException($"Failed to move to mini FAT chain index: {chainIndex}.");

        FatStream miniStream = Context.MiniStream;
        int writeCount = 0;
        do
        {
            MiniSector miniSector = miniChain.CurrentSector;
            long basePosition = miniSector.Position + sectorOffset;
            miniStream.Seek(basePosition, SeekOrigin.Begin);
            int remaining = buffer.Length - writeCount;
            int localOffset = writeCount;
            long writeLength = Math.Min(remaining, Context.MiniSectorSize - sectorOffset);
            ReadOnlySpan<byte> slice = buffer.Slice(localOffset, (int)writeLength);
            miniStream.Write(slice);
            position += writeLength;
            writeCount += (int)writeLength;
            if (position > Length)
                DirectoryEntry.StreamLength = position;
            sectorOffset = 0;
            if (writeCount >= buffer.Length)
                return;
        } while (miniChain.MoveNext());

        // Should be impossible since the chain length was validated
        throw new InvalidOperationException($"The end of mini FAT chain was reached.");
    }

#endif
}
