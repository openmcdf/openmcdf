namespace OpenMcdf.Tests;

[TestClass]
public sealed class HeaderTests
{
    [TestMethod]
    public void MajorVersion_Invalid()
    {
        Header header = new();
        Assert.ThrowsExactly<FileFormatException>(() => header.MajorVersion = 5);
        Assert.ThrowsExactly<FileFormatException>(() => new Header(Version.Unknown));
    }

    [TestMethod]
    public void SectorShift_InvalidForMajorVersion()
    {
        Header v3 = new(Version.V3);
        Assert.ThrowsExactly<FileFormatException>(() => v3.SectorShift = Header.SectorShiftV4);
        Header v4 = new(Version.V4);
        Assert.ThrowsExactly<FileFormatException>(() => v4.SectorShift = Header.SectorShiftV3);
    }

    [TestMethod]
    public void MiniSectorShift_Invalid()
    {
        Header header = new();
        Assert.ThrowsExactly<FileFormatException>(() => header.MiniSectorShift = 7);
    }

    [TestMethod]
    public void EqualsAndHashCode()
    {
        Header a = new(Version.V3);
        Header b = a.Clone();
        Assert.IsTrue(a.Equals((object)b));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsFalse(a.Equals((object?)null));
        Assert.IsFalse(a.Equals("header"));
        b.DifatSectorCount = 1;
        Assert.IsFalse(a.Equals(b));
    }
}
