namespace OpenMcdf.Tests;

[TestClass]
public sealed class BinaryWriterTests
{
    [TestMethod]
    public void WriteGuid()
    {
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
        Guid expectedGuid = new(bytes);
        using MemoryStream stream = new(bytes);
        using (CfbBinaryWriter writer = new(stream))
            writer.Write(expectedGuid);

        stream.Position = 0;
        using CfbBinaryReader reader = new(stream);
        Guid actualGuid = reader.ReadGuid();

        Assert.AreEqual(expectedGuid, actualGuid);
    }

    [TestMethod]
    [DataRow("TestStream_v3_0.cfs")]
    [DataRow("TestStream_v4_0.cfs")]
    public void WriteHeader(string fileName)
    {
        using FileStream stream = File.OpenRead(fileName);
        using CfbBinaryReader reader = new(stream);
        Header header = reader.ReadHeader();

        using MemoryStream memoryStream = new();
        using CfbBinaryWriter writer = new(memoryStream);
        writer.Write(header);

        memoryStream.Position = 0;
        using CfbBinaryReader reader2 = new(memoryStream);
        Header actualHeader = reader2.ReadHeader();

        Assert.AreEqual(header, actualHeader);
    }

    [TestMethod]
    public void WriteDirectoryEntry()
    {
        DirectoryEntry expected = new()
        {
            Type = StorageType.Stream,
            Color = NodeColor.Black,
            LeftSiblingId = 2,
            RightSiblingId = 3,
            ChildId = 4,
            CLSID = new("D295800C-F486-497A-9C29-A049227E7CB1"),
            StateBits = 5,
            CreationTime = FileTime.UtcZero,
            ModifiedTime = FileTime.UtcZero,
            StartSectorId = 6,
            StreamLength = 7,
            NameString = "Root Entry",
        };

        using MemoryStream stream = new();
        using CfbBinaryWriter writer = new(stream);
        writer.Write(expected);

        stream.Position = 0;
        using CfbBinaryReader reader = new(stream);
        DirectoryEntry actual = reader.ReadDirectoryEntry(Version.V4, 0);

        Assert.AreEqual(expected, actual);
    }
}
