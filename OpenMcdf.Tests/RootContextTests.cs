namespace OpenMcdf.Tests;

[TestClass]
public sealed class RootContextTests
{
    [TestMethod]
    public void Writer_ReadOnly()
    {
        using MemoryStream stream = new();
        using (var root = RootStorage.Create(stream, Version.V3, StorageModeFlags.LeaveOpen))
        {
        }

        using MemoryStream readOnlyStream = new(stream.ToArray(), false);
        using var readOnlyRoot = RootStorage.Open(readOnlyStream);
        Assert.IsFalse(readOnlyRoot.CanWrite);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = readOnlyRoot.Context.Writer);
    }

    [TestMethod]
    public void Flush_NoUsedSectorsThrowsFileFormatException()
    {
        using var root = RootStorage.CreateInMemory();
        Fat fat = root.Context.Fat;
        var entries = fat.ToList();
        try
        {
            foreach (FatEntry entry in entries)
                fat[entry.Index] = SectorType.Free;
            Assert.ThrowsExactly<FileFormatException>(() => root.Flush());
        }
        finally
        {
            foreach (FatEntry entry in entries)
                fat[entry.Index] = entry.Value;
        }
    }

    [TestMethod]
    public void BinaryWriter_Position()
    {
        using MemoryStream stream = new(new byte[16]);
        using CfbBinaryWriter writer = new(stream);
        writer.Position = 4;
        Assert.AreEqual(4, writer.Position);
    }
}
