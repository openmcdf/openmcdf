namespace OpenMcdf.Tests;

[TestClass]
public sealed class CfbBinaryReaderTests
{
    const int EntryLength = 128;

    static byte[] ReadHeaderBytes(string fileName) => File.ReadAllBytes(fileName).AsSpan(0, 512).ToArray();

    static Header ReadHeader(byte[] bytes, bool strict = false)
    {
        using MemoryStream stream = new(bytes);
        using CfbBinaryReader reader = new(stream, strict);
        return reader.ReadHeader();
    }

    static byte[] CreateEntryBytes(
        StorageType type,
        bool v4 = false,
        ushort nameLength = 4,
        NodeColor color = NodeColor.Black,
        uint left = StreamId.NoStream,
        uint right = StreamId.NoStream,
        uint child = StreamId.NoStream,
        ulong creationTime = 0,
        ulong modifiedTime = 0,
        uint startSector = 0,
        long streamLength = 0)
    {
        byte[] bytes = new byte[EntryLength];
        bytes[0] = (byte)'A';
        BitConverter.GetBytes(nameLength).CopyTo(bytes, 64);
        bytes[66] = (byte)type;
        bytes[67] = (byte)color;
        BitConverter.GetBytes(left).CopyTo(bytes, 68);
        BitConverter.GetBytes(right).CopyTo(bytes, 72);
        BitConverter.GetBytes(child).CopyTo(bytes, 76);
        BitConverter.GetBytes(creationTime).CopyTo(bytes, 100);
        BitConverter.GetBytes(modifiedTime).CopyTo(bytes, 108);
        BitConverter.GetBytes(startSector).CopyTo(bytes, 116);
        if (v4)
            BitConverter.GetBytes(streamLength).CopyTo(bytes, 120);
        else
            BitConverter.GetBytes((uint)streamLength).CopyTo(bytes, 120);
        return bytes;
    }

    static DirectoryEntry ReadEntry(byte[] bytes, Version version = Version.V3, bool strict = false, long maxStreamLength = long.MaxValue)
    {
        using MemoryStream stream = new(bytes);
        using CfbBinaryReader reader = new(stream, strict);
        return reader.ReadDirectoryEntry(version, 1, maxStreamLength);
    }

    [TestMethod]
    public void Position()
    {
        using MemoryStream stream = new(new byte[16]);
        using CfbBinaryReader reader = new(stream);
        reader.Position = 8;
        Assert.AreEqual(8, stream.Position);
        Assert.AreEqual(8, reader.Position);
    }

    [TestMethod]
    public void ReadHeader_UnexpectedMinorVersionIsAccepted()
    {
        byte[] bytes = ReadHeaderBytes("TestStream_v3_0.cfs");
        bytes[24] = 0x01;
        Header header = ReadHeader(bytes);
        Assert.AreEqual(1, header.MinorVersion);
    }

    [TestMethod]
    public void ReadHeader_InvalidMajorVersion()
    {
        byte[] bytes = ReadHeaderBytes("TestStream_v3_0.cfs");
        bytes[26] = 0x05;
        Assert.ThrowsExactly<FileFormatException>(() => ReadHeader(bytes));
    }

    [TestMethod]
    public void ReadHeader_InvalidByteOrder()
    {
        byte[] bytes = ReadHeaderBytes("TestStream_v3_0.cfs");
        bytes[28] = 0x00;
        Assert.ThrowsExactly<FileFormatException>(() => ReadHeader(bytes));
    }

    [TestMethod]
    [DataRow("TestStream_v3_0.cfs")]
    [DataRow("TestStream_v4_0.cfs")]
    public void ReadHeader_InvalidSectorShift(string fileName)
    {
        byte[] bytes = ReadHeaderBytes(fileName);
        bytes[30] = 0x0A;
        bytes[31] = 0x00;
        Assert.ThrowsExactly<FileFormatException>(() => ReadHeader(bytes));
    }

    [TestMethod]
    public void ReadHeader_InvalidMiniSectorShift()
    {
        byte[] bytes = ReadHeaderBytes("TestStream_v3_0.cfs");
        bytes[32] = 0x07;
        Assert.ThrowsExactly<FileFormatException>(() => ReadHeader(bytes));
    }

    [TestMethod]
    public void ReadHeader_InvalidMiniStreamCutoffSize()
    {
        byte[] bytes = ReadHeaderBytes("TestStream_v3_0.cfs");
        bytes[56] = 0x01;
        Assert.ThrowsExactly<FileFormatException>(() => ReadHeader(bytes));
    }

    [TestMethod]
    public void ReadStorageType_Invalid()
    {
        using MemoryStream stream = new([3]);
        using CfbBinaryReader reader = new(stream);
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadStorageType());
    }

    [TestMethod]
    public void ReadStorageType_Unallocated()
    {
        using MemoryStream stream = new([0]);
        using CfbBinaryReader reader = new(stream);
        Assert.AreEqual(StorageType.Unallocated, reader.ReadStorageType());
    }

    [TestMethod]
    public void ReadColor_Invalid()
    {
        using MemoryStream stream = new([2]);
        using CfbBinaryReader reader = new(stream);
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadColor());
    }

    [TestMethod]
    public void ReadDirectoryEntry_UnsupportedVersion()
    {
        using MemoryStream stream = new(new byte[EntryLength]);
        using CfbBinaryReader reader = new(stream);
        Assert.ThrowsExactly<ArgumentException>(() => reader.ReadDirectoryEntry(Version.Unknown, 0, long.MaxValue));
    }

    [TestMethod]
    public void ReadDirectoryEntry_Valid()
    {
        DirectoryEntry v3 = ReadEntry(CreateEntryBytes(StorageType.Stream, startSector: 3, streamLength: 10), Version.V3, true);
        Assert.AreEqual(10, v3.StreamLength);
        DirectoryEntry v4 = ReadEntry(CreateEntryBytes(StorageType.Stream, v4: true, startSector: 3, streamLength: 10), Version.V4, true);
        Assert.AreEqual(10, v4.StreamLength);
        Assert.AreEqual(StorageType.Unallocated, ReadEntry(CreateEntryBytes(StorageType.Unallocated)).Type);
    }

    [TestMethod]
    public void ReadDirectoryEntry_InvalidType()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes((StorageType)3)));
    }

    [TestMethod]
    public void ReadDirectoryEntry_InvalidColor()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, color: (NodeColor)2)));
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(66)]
    public void ReadDirectoryEntry_StrictInvalidNameLength(int nameLength)
    {
        byte[] bytes = CreateEntryBytes(StorageType.Stream, nameLength: (ushort)nameLength);
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(bytes, strict: true));
    }

    [TestMethod]
    public void ReadDirectoryEntry_StrictNameNotNullTerminated()
    {
        byte[] bytes = CreateEntryBytes(StorageType.Stream, nameLength: 2);
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(bytes, strict: true));
    }

    [TestMethod]
    public void ReadDirectoryEntry_InvalidSiblingIds()
    {
        const uint id = 0xFFFFFFFB;
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, left: id)));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, right: id)));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, child: id)));
    }

    [TestMethod]
    public void ReadDirectoryEntry_StrictTimes()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, creationTime: 1), strict: true));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Root, creationTime: 1), strict: true));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, modifiedTime: 1), strict: true));
        Assert.AreEqual(1UL, ReadEntry(CreateEntryBytes(StorageType.Stream, modifiedTime: 1)).ModifiedTime);
        Assert.AreEqual(1UL, ReadEntry(CreateEntryBytes(StorageType.Storage, creationTime: 1, modifiedTime: 1), strict: true).ModifiedTime);
    }

    [TestMethod]
    public void ReadDirectoryEntry_InvalidStreamStartSector()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, startSector: 0xFFFFFFFB)));
    }

    [TestMethod]
    [DataRow(0u, true)]
    [DataRow(SectorType.EndOfChain, true)]
    [DataRow(StreamId.NoStream, true)]
    [DataRow(1u, false)]
    public void ReadDirectoryEntry_StorageStartSector(uint startSector, bool valid)
    {
        byte[] bytes = CreateEntryBytes(StorageType.Storage, startSector: startSector);
        ReadEntry(bytes);
        if (valid)
            ReadEntry(bytes, strict: true);
        else
            Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(bytes, strict: true));
    }

    [TestMethod]
    public void ReadDirectoryEntry_StreamLength()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, v4: true, streamLength: -1), Version.V4));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, streamLength: 0x80000001), Version.V3));
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(CreateEntryBytes(StorageType.Stream, v4: true, streamLength: 101), Version.V4, maxStreamLength: 100));
    }

    [TestMethod]
    public void ReadDirectoryEntry_StorageStreamLength()
    {
        byte[] bytes = CreateEntryBytes(StorageType.Storage, streamLength: 1);
        Assert.AreEqual(1, ReadEntry(bytes).StreamLength);
        Assert.ThrowsExactly<FileFormatException>(() => ReadEntry(bytes, strict: true));
    }
}
