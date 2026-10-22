namespace OpenMcdf.Tests;

[TestClass]
public sealed class DirectoryTreeValidatorTests
{
    static DirectoryEntry CreateEntry(string name)
    {
        DirectoryEntry entry = new();
        entry.NameString = name;
        return entry;
    }

    [TestMethod]
    public void SearchOrderValidator()
    {
        DirectoryTreeSearchOrderValidator validator = new();
        DirectoryEntry b = CreateEntry("b");
        DirectoryEntry a = CreateEntry("a");
        DirectoryEntry c = CreateEntry("c");

        validator.Validate(null, a, SiblingType.Left);
        validator.Validate(b, null, SiblingType.Left);

        validator.Validate(b, a, SiblingType.Left);
        validator.Reset();
        validator.Validate(b, c, SiblingType.Right);
        validator.Reset();

        Assert.ThrowsExactly<FileFormatException>(() => validator.Validate(b, c, SiblingType.Left));
        validator.Reset();
        Assert.ThrowsExactly<FileFormatException>(() => validator.Validate(b, a, SiblingType.Right));
    }

    [TestMethod]
    public void TraversalOrderValidator()
    {
        DirectoryTreeTraversalOrderValidator validator = new();
        DirectoryEntry b = CreateEntry("b");
        DirectoryEntry a = CreateEntry("a");
        DirectoryEntry c = CreateEntry("c");

        validator.Reset();
        validator.Validate(null, a, SiblingType.Left);
        validator.Validate(b, null, SiblingType.Left);
        validator.Validate(b, a, SiblingType.Left);
        validator.Validate(b, c, SiblingType.Right);
        Assert.ThrowsExactly<FileFormatException>(() => validator.Validate(b, c, SiblingType.Left));
        Assert.ThrowsExactly<FileFormatException>(() => validator.Validate(b, a, SiblingType.Right));
    }
}
