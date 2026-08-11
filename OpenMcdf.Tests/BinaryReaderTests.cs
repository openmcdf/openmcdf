namespace OpenMcdf.Tests;

[TestClass]
public sealed class BinaryReaderTests
{
    [TestMethod]
    public void ReadGuid()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        using MemoryStream stream = new(bytes);
        using CfbBinaryReader reader = new(stream);
        Guid guid = reader.ReadGuid();
        Assert.AreEqual(new Guid(bytes), guid);

        Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadGuid());
    }

    [TestMethod]
    [DataRow("TestStream_v3_0.cfs")]
    [DataRow("TestStream_v4_0.cfs")]
    public void ReadHeader(string fileName)
    {
        using FileStream stream = File.OpenRead(fileName);
        using MemoryStream memoryStream = new();
        stream.CopyAllTo(memoryStream);

        using CfbBinaryReader reader = new(memoryStream);
        Header header = reader.ReadHeader();

        stream.CopyAllTo(memoryStream);
        memoryStream.WriteByte(1); // Corrupt signature
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadHeader());

        stream.CopyAllTo(memoryStream);
        memoryStream.Position = 24;
        memoryStream.WriteByte(1); // Corrupt CLSID
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadHeader());

        stream.CopyAllTo(memoryStream);
        memoryStream.Position = 26;
        memoryStream.WriteByte(1); // Corrupt Major version
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadHeader());

        stream.CopyAllTo(memoryStream);
        memoryStream.Position = 28;
        memoryStream.WriteByte(1); // Corrupt byte order
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadHeader());

        stream.CopyAllTo(memoryStream);
        memoryStream.Position = 32;
        memoryStream.WriteByte(1); // Corrupt mini sector shift
        Assert.ThrowsExactly<FileFormatException>(() => reader.ReadHeader());
    }
}
