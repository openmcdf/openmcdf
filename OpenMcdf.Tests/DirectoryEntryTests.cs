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

    [TestMethod]
    [DataRow(0, "")]
    [DataRow(1, "")]
    [DataRow(2, "")]
    [DataRow(3, "")]
    [DataRow(4, "0")]
    [DataRow(5, "0")]
    [DataRow(64, "0123456789012345678901234567890")]
    [DataRow(65, "0123456789012345678901234567890")]
    [DataRow(66, "0123456789012345678901234567890")]
    public void ClampedNameLength(int nameLength, string expectedName)
    {
        DirectoryEntry entry = new()
        {
            NameString = "0123456789012345678901234567890",
        };
        entry.Name[31] = 'x';
        entry.NameLength = (ushort)nameLength;

        Assert.AreEqual(expectedName, entry.NameString);
        Assert.AreEqual(expectedName.Length, entry.NameCharSpan.Length);
        Assert.AreEqual(0, DirectoryEntryComparer.Default.Compare(new DirectoryEntry { NameString = expectedName }, entry));
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(66)]
    public void NonStrictMalformedNameLengthCanBeOpenedAfterEnumeration(int nameLength)
    {
        using MemoryStream stream = new();
        using (var rootStorage = RootStorage.Create(stream, Version.V3, StorageModeFlags.LeaveOpen))
        {
            using CfbStream childStream = rootStorage.CreateStream("0123456789012345678901234567890");
            childStream.WriteByte(0x42);
        }

        using (var rootStorage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen))
        {
            DirectoryEntry entry = rootStorage.Context.DirectoryEntries.GetDirectoryEntry(1);
            entry.Name[31] = 'x';
            entry.NameLength = (ushort)nameLength;
            rootStorage.Context.DirectoryEntries.Write(entry);
        }

        using (var rootStorage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen))
        {
            EntryInfo entryInfo = rootStorage.EnumerateEntries().Single();
            Assert.IsTrue(rootStorage.ContainsEntry(entryInfo.Name));
            using CfbStream childStream = rootStorage.OpenStream(entryInfo.Name);
            Assert.AreEqual(0x42, childStream.ReadByte());
        }
    }
}
