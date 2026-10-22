namespace OpenMcdf.Tests;

[TestClass]
public sealed class ThrowHelperTests
{
    sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    [TestMethod]
    public void ThrowIfStreamArgumentsAreInvalid()
    {
        byte[] buffer = new byte[4];
        ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, 0, 4);
        Assert.ThrowsExactly<ArgumentNullException>(() => ThrowHelper.ThrowIfStreamArgumentsAreInvalid(null!, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, -1, 0));
        Assert.ThrowsExactly<ArgumentException>(() => ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, 2, 3));
        Assert.ThrowsExactly<ArgumentException>(() => ThrowHelper.ThrowIfStreamArgumentsAreInvalid(buffer, 0, -1));
    }

    [TestMethod]
    public void ThrowIfSeekingNotSupported()
    {
        using MemoryStream seekable = new();
        seekable.ThrowIfSeekingNotSupported();
        using NonSeekableStream nonSeekable = new();
        Assert.ThrowsExactly<ArgumentException>(() => nonSeekable.ThrowIfSeekingNotSupported());
    }

    [TestMethod]
    public void ThrowIfNameIsInvalid()
    {
        ThrowHelper.ThrowIfNameIsInvalid("name");
        Assert.ThrowsExactly<ArgumentNullException>(() => ThrowHelper.ThrowIfNameIsInvalid(null!));
        Assert.ThrowsExactly<ArgumentException>(() => ThrowHelper.ThrowIfNameIsInvalid("a/b"));
        Assert.ThrowsExactly<ArgumentException>(() => ThrowHelper.ThrowIfNameIsInvalid(new string('a', 32)));
    }

    [TestMethod]
    public void ThrowIfSectorIdIsInvalid()
    {
        ThrowHelper.ThrowIfSectorIdIsInvalid(SectorType.Maximum);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowHelper.ThrowIfSectorIdIsInvalid(SectorType.Maximum + 1));
    }

    [TestMethod]
    public void ThrowIfStreamIdIsInvalid()
    {
        ThrowHelper.ThrowIfStreamIdIsInvalid(StreamId.NoStream);
        Assert.ThrowsExactly<FileFormatException>(() => ThrowHelper.ThrowIfStreamIdIsInvalid(StreamId.Maximum + 1));
        ThrowHelper.ThrowIfStreamIdIsInvalidInPractice(SectorType.EndOfChain);
        Assert.ThrowsExactly<FileFormatException>(() => ThrowHelper.ThrowIfStreamIdIsInvalidInPractice(StreamId.Maximum + 1));
    }

    [TestMethod]
    public void StreamId_IsValid()
    {
        Assert.IsTrue(StreamId.IsValid(0));
        Assert.IsTrue(StreamId.IsValid(StreamId.NoStream));
        Assert.IsFalse(StreamId.IsValid(StreamId.Maximum + 1));
        Assert.IsFalse(StreamId.IsValid(SectorType.EndOfChain));
        Assert.IsTrue(StreamId.IsValidInPractice(SectorType.EndOfChain));
        Assert.IsFalse(StreamId.IsValidInPractice(StreamId.Maximum + 1));
    }

    [TestMethod]
    public void ThrowIfEnumerationNotStarted()
    {
        ThrowHelper.ThrowIfEnumerationNotStarted(true);
        Assert.ThrowsExactly<InvalidOperationException>(() => ThrowHelper.ThrowIfEnumerationNotStarted(false));
    }

    [TestMethod]
    public void ThrowIfDirectoryEntryStreamIdIsInvalid()
    {
        ThrowHelper.ThrowIfDirectoryEntryStreamIdIsInvalid(StreamId.Maximum);
        Assert.ThrowsExactly<FileFormatException>(() => ThrowHelper.ThrowIfDirectoryEntryStreamIdIsInvalid(StreamId.Maximum + 1));
    }

    [TestMethod]
    public void ThrowDirectoryEntryNotFound()
    {
        Assert.ThrowsExactly<FileFormatException>(() => ThrowHelper.ThrowDirectoryEntryNotFound(5));
    }

    [TestMethod]
    public void FileFormatException_Constructors()
    {
        Assert.IsNotNull(new FileFormatException().Message);
        InvalidOperationException inner = new();
        FileFormatException exception = new("message", inner);
        Assert.AreEqual("message", exception.Message);
        Assert.AreSame(inner, exception.InnerException);
    }

    [TestMethod]
    public void FileTime_IsFileTimeUtcZero()
    {
        Assert.IsTrue(FileTime.UtcZero.IsFileTimeUtcZero());
        Assert.IsFalse(DateTime.UtcNow.IsFileTimeUtcZero());
    }
}
