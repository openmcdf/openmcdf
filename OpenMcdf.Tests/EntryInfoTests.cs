namespace OpenMcdf.Tests;

[TestClass]
public sealed class EntryInfoTests
{
    [TestMethod]
    [DataRow("MultipleStorage.cfs", EntryType.Storage, "MyStorage", "/Root Entry")]
    [DataRow("TestStream_v3_0.cfs", EntryType.Stream, "TestStream", "/Root Entry")]
    [DataRow("TestStream_v4_0.cfs", EntryType.Stream, "TestStream", "/Root Entry")]
    public void EnumerateEntryInfos(string fileName, EntryType type, string name, string path)
    {
        using var rootStorage = RootStorage.OpenRead(fileName);
        IEnumerable<EntryInfo> entries = rootStorage.EnumerateEntries();
        Assert.HasCount(1, entries);

        EntryInfo entry = entries.First();
        Assert.AreEqual(type, entry.Type);
        Assert.AreEqual(name, entry.Name);
        Assert.AreEqual(path, entry.Path);
        Assert.AreEqual(Guid.Empty, entry.CLSID);
        Assert.AreEqual(0, entry.Length);
        if (type is EntryType.Storage)
        {
            Assert.AreNotEqual(FileTime.UtcZero, entry.CreationTime);
            Assert.AreNotEqual(FileTime.UtcZero, entry.ModifiedTime);
        }
        else
        {
            Assert.AreEqual(FileTime.UtcZero, entry.CreationTime);
            Assert.AreEqual(FileTime.UtcZero, entry.ModifiedTime);
        }
    }
}
