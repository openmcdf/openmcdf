namespace OpenMcdf;

/// <summary>
/// Stores modifications to a CFB stream that can be committed or reverted.
/// </summary>
internal sealed class TransactedStream : Stream
{
    readonly RootContextSite rootContextSite;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Non-dispose ownership")]
    readonly Stream originalStream;
    readonly Dictionary<uint, long> dirtySectorPositions = new();
    readonly byte[] buffer;

    RootContext Context => rootContextSite.Context;

    public TransactedStream(RootContextSite rootContextSite, Stream originalStream, Stream overlayStream)
    {
        this.rootContextSite = rootContextSite;
        this.originalStream = originalStream;
        OverlayStream = overlayStream;
        buffer = new byte[Context.SectorSize];
    }

    protected override void Dispose(bool disposing)
    {
        // Original stream might be owned by the caller
        OverlayStream.Dispose();

        base.Dispose(disposing);
    }

    public Stream OverlayStream { get; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => OverlayStream.Length;

    public override long Position
    {
        get => originalStream.Position;
        set => originalStream.Position = value;
    }

    public override void Flush() => OverlayStream.Flush();

    uint GetFatChainIndexAndSectorOffset(long offset, out long sectorOffset) => (uint)Math.DivRem(offset, Context.SectorSize, out sectorOffset);

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, offset, count);

        int read;
        int totalRead = 0;
        do
        {
            uint sectorId = GetFatChainIndexAndSectorOffset(originalStream.Position, out long sectorOffset);
            int remainingFromSector = Context.SectorSize - (int)sectorOffset;
            int localCount = Math.Min(count - totalRead, remainingFromSector);

            if (dirtySectorPositions.TryGetValue(sectorId, out long overlayPosition))
            {
                OverlayStream.Position = overlayPosition + sectorOffset;
                read = OverlayStream.Read(buffer, offset + totalRead, localCount);
                originalStream.Seek(read, SeekOrigin.Current);
            }
            else
            {
                read = originalStream.Read(buffer, offset + totalRead, localCount);
            }

            if (read == 0)
                break;

            totalRead += read;
        } while (totalRead < count);

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => originalStream.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, offset, count);

        int totalWritten = 0;
        while (totalWritten < count)
        {
            uint sectorId = GetFatChainIndexAndSectorOffset(originalStream.Position, out long sectorOffset);
            int remainingFromSector = Context.SectorSize - (int)sectorOffset;
            int localCount = Math.Min(count - totalWritten, remainingFromSector);

            bool added = false;
            if (!dirtySectorPositions.TryGetValue(sectorId, out long overlayPosition))
            {
                overlayPosition = OverlayStream.Length;
                dirtySectorPositions.Add(sectorId, overlayPosition);
                added = true;
            }

            long originalPosition = originalStream.Position;
            if (added && localCount != Context.SectorSize && originalPosition < originalStream.Length)
            {
                // Copy the existing sector data
                originalStream.Position = originalPosition - sectorOffset;
                originalStream.ReadExactly(this.buffer, 0, this.buffer.Length);

                OverlayStream.Position = overlayPosition;
                OverlayStream.Write(this.buffer, 0, this.buffer.Length);
            }

            OverlayStream.Position = overlayPosition + sectorOffset;
            OverlayStream.Write(buffer, offset + totalWritten, localCount);
            if (OverlayStream.Length < overlayPosition + Context.SectorSize)
                OverlayStream.SetLength(overlayPosition + Context.SectorSize);
            originalStream.Position = originalPosition + localCount;
            totalWritten += localCount;
        }
    }

    public void Commit()
    {
        foreach (KeyValuePair<uint, long> entry in dirtySectorPositions)
        {
            OverlayStream.Position = entry.Value;
            OverlayStream.ReadExactly(buffer, 0, buffer.Length);

            originalStream.Position = entry.Key * Context.SectorSize;
            originalStream.Write(buffer, 0, buffer.Length);
        }

        originalStream.Flush();
        dirtySectorPositions.Clear();
    }

    public void Revert()
    {
        dirtySectorPositions.Clear();
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK

    public override int ReadByte() => this.ReadByteCore();

    public override int Read(Span<byte> buffer)
    {
        uint sectorId = (uint)Math.DivRem(originalStream.Position, Context.SectorSize, out long sectorOffset);
        int remainingFromSector = Context.SectorSize - (int)sectorOffset;
        int localCount = Math.Min(buffer.Length, remainingFromSector);
        System.Diagnostics.Debug.Assert(localCount == buffer.Length);

        Span<byte> slice = buffer[..localCount];
        int read;
        if (dirtySectorPositions.TryGetValue(sectorId, out long overlayPosition))
        {
            OverlayStream.Position = overlayPosition + sectorOffset;
            read = OverlayStream.Read(slice);
            originalStream.Seek(read, SeekOrigin.Current);
        }
        else
        {
            read = originalStream.Read(slice);
        }

        return read;
    }

    public override void WriteByte(byte value) => this.WriteByteCore(value);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        int totalWritten = 0;
        while (totalWritten < buffer.Length)
        {
            uint sectorId = (uint)Math.DivRem(originalStream.Position, Context.SectorSize, out long sectorOffset);
            int remainingFromSector = Context.SectorSize - (int)sectorOffset;
            int localCount = Math.Min(buffer.Length - totalWritten, remainingFromSector);

            bool added = false;
            if (!dirtySectorPositions.TryGetValue(sectorId, out long overlayPosition))
            {
                overlayPosition = OverlayStream.Length;
                dirtySectorPositions.Add(sectorId, overlayPosition);
                added = true;
            }

            long originalPosition = originalStream.Position;
            if (added && localCount != Context.SectorSize && originalPosition < originalStream.Length)
            {
                // Copy the existing sector data
                originalStream.Position = originalPosition - sectorOffset;
                originalStream.ReadExactly(this.buffer);

                OverlayStream.Position = overlayPosition;
                OverlayStream.Write(this.buffer, 0, this.buffer.Length);
            }

            OverlayStream.Position = overlayPosition + sectorOffset;
            OverlayStream.Write(buffer.Slice(totalWritten, localCount));
            if (OverlayStream.Length < overlayPosition + Context.SectorSize)
                OverlayStream.SetLength(overlayPosition + Context.SectorSize);
            originalStream.Position = originalPosition + localCount;
            totalWritten += localCount;
        }
    }

#endif
}
