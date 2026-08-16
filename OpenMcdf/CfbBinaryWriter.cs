using System.Text;

namespace OpenMcdf;

/// <summary>
/// Writes CFB data types to a stream.
/// </summary>
internal sealed class CfbBinaryWriter : BinaryWriter
{
    public CfbBinaryWriter(Stream input)
        : base(input, Encoding.Unicode, true)
    {
    }

    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

#if !NETSTANDARD2_0 && !NETFRAMEWORK

    public override void Write(ReadOnlySpan<byte> buffer) => BaseStream.Write(buffer);

#endif

    public void Write(in Guid value)
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        byte[] bytes = value.ToByteArray();
        Write(bytes);
#else
        Span<byte> localBuffer = stackalloc byte[16];
        value.TryWriteBytes(localBuffer);
        Write(localBuffer);
#endif
    }

    public void Write(Header header)
    {
        Write(Header.Signature);
        Write(header.CLSID);
        Write(header.MinorVersion);
        Write(header.MajorVersion);
        Write(Header.LittleEndian);
        Write(header.SectorShift);
        Write(header.MiniSectorShift);
        Write(Header.Unused);
        Write(header.DirectorySectorCount);
        Write(header.FatSectorCount);
        Write(header.FirstDirectorySectorId);
        Write(header.TransactionSignatureNumber);
        Write(Header.MiniStreamCutoffSize);
        Write(header.FirstMiniFatSectorId);
        Write(header.MiniFatSectorCount);
        Write(header.FirstDifatSectorId);
        Write(header.DifatSectorCount);
        for (int i = 0; i < Header.DifatArrayLength; i++)
            Write(header.Difat[i]);
    }

    public void Write(DirectoryEntry entry)
    {
        Write(entry.Name, 0, DirectoryEntry.NameFieldLength);
        Write(entry.NameLength);
        Write((byte)entry.Type);
        Write((byte)NodeColor.Black); // TODO: Support tree balancing
        Write(entry.LeftSiblingId);
        Write(entry.RightSiblingId);
        Write(entry.ChildId);
        Write(entry.CLSID);
        Write(entry.StateBits);
        Write(entry.CreationTime);
        Write(entry.ModifiedTime);
        Write(entry.StartSectorId);
        Write(entry.StreamLength);
    }
}
