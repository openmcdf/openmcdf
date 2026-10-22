namespace OpenMcdf.Tests;

[TestClass]
public sealed class DirectoryEntryTests
{
    [TestMethod]
    [DataRow("", "", 0)]
    [DataRow("", "longer", -1)]
    [DataRow("longer", "", 1)]
    [DataRow("a", "a", 0)]
    [DataRow("a", "b", -1)]
    [DataRow("b", "a", 1)]
    public void EnumerateEntryInfos(string nameX, string nameY, int expectedCompare)
    {
        DirectoryEntry entryX = new()
        {
            NameString = nameX,
        };

        DirectoryEntry entryY = new()
        {
            NameString = nameY,
        };

        int actualCompare = DirectoryEntryComparer.Default.Compare(entryX, entryY);
        Assert.AreEqual(expectedCompare, actualCompare);
    }

    [TestMethod]
    public void Compare_Nulls()
    {
        DirectoryEntry entry = new() { NameString = "a" };
        DirectoryEntry? missingX = null;
        DirectoryEntry? missingY = null;
        Assert.AreEqual(0, DirectoryEntryComparer.Default.Compare(missingX, missingY));
        Assert.AreEqual(-1, DirectoryEntryComparer.Default.Compare(missingX, entry));
        Assert.AreEqual(1, DirectoryEntryComparer.Default.Compare(entry, missingY));
    }

    [TestMethod]
    public void ColorChar()
    {
        Assert.AreEqual('R', new DirectoryEntry { Color = NodeColor.Red }.ColorChar);
        Assert.AreEqual('B', new DirectoryEntry { Color = NodeColor.Black }.ColorChar);
        Assert.AreEqual('?', new DirectoryEntry { Color = (NodeColor)5 }.ColorChar);
    }

    [TestMethod]
    public void EntryType()
    {
        Assert.AreEqual(OpenMcdf.EntryType.Stream, new DirectoryEntry { Type = StorageType.Stream }.EntryType);
        Assert.AreEqual(OpenMcdf.EntryType.Storage, new DirectoryEntry { Type = StorageType.Storage }.EntryType);
        Assert.AreEqual(OpenMcdf.EntryType.Storage, new DirectoryEntry { Type = StorageType.Root }.EntryType);
        Assert.ThrowsExactly<FileFormatException>(() => _ = new DirectoryEntry().EntryType);
    }

    [TestMethod]
    public void EqualsAndHashCode()
    {
        DirectoryEntry a = new() { Id = 1, NameString = "a" };
        DirectoryEntry b = new() { Id = 1, NameString = "a" };
        Assert.IsTrue(a.Equals((object)b));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsFalse(a.Equals((object?)null));
        Assert.IsFalse(a.Equals("a"));

        b.StreamLength = 1;
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "b" };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 2, NameString = "a" };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", Type = StorageType.Stream };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", Color = NodeColor.Black };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", LeftSiblingId = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", RightSiblingId = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", ChildId = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", CLSID = Guid.NewGuid() };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", StateBits = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", CreationTime = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", ModifiedTime = 1 };
        Assert.IsFalse(a.Equals(b));
        b = new() { Id = 1, NameString = "a", StartSectorId = 1 };
        Assert.IsFalse(a.Equals(b));
    }
}
