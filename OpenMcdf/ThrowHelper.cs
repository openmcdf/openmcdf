using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace OpenMcdf;

/// <summary>
/// Extensions to consistently throw exceptions.
/// </summary>
internal static class ThrowHelper
{
    static readonly char[] InvalidNameChars = ['\\', '/', ':', '!'];

    public static void ThrowIfDisposed(this object instance, bool disposed)
    {
        if (disposed)
            throw new ObjectDisposedException(instance.GetType().FullName);
    }

    public static void ThrowIfStreamArgumentsAreInvalid(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be a non-negative number.");

        if ((uint)count > buffer.Length - offset)
            throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }

    public static void ThrowIfSeekingNotSupported(this Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must support seeking.", nameof(stream));
    }

    public static void ThrowIfNotWritable(this Stream stream)
    {
        if (!stream.CanWrite)
            throw new NotSupportedException("Stream does not support writing.");
    }

    public static void ThrowSeekBeforeOrigin() => throw new IOException("An attempt was made to move the position before the beginning of the stream.");

    public static void ThrowIfNameIsInvalid(string value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        if (value.AsSpan().ContainsAny(InvalidNameChars))
            throw new ArgumentException("Name cannot contain any of the following characters: '\\', '/', ':', '!'.", nameof(value));

        if (Encoding.Unicode.GetByteCount(value) > DirectoryEntry.NameFieldLength - 2)
            throw new ArgumentException($"{value} exceeds maximum encoded length of {DirectoryEntry.NameFieldLength} bytes.", nameof(value));
    }

    public static void ThrowIfSectorIdIsInvalid(uint value)
    {
        if (value > SectorType.Maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"Invalid sector ID: {value:X8}.");
    }

    public static void ThrowIfStreamIdIsInvalid(uint value)
    {
        if (!StreamId.IsValid(value))
            throw new FileFormatException($"Invalid stream ID: {value:X8}.");
    }

    public static void ThrowIfStreamIdIsInvalidInPractice(uint value)
    {
        if (!StreamId.IsValidInPractice(value))
            throw new FileFormatException($"Invalid stream ID: {value:X8}.");
    }

    public static void ThrowIfInvalidBinarySearchTree(bool condition)
    {
        if (condition)
            throw new FileFormatException("Directory entries are not in a valid binary search tree.");
    }

    public static void ThrowIfInvalidColor(DirectoryEntry entry, DirectoryEntry sibling)
    {
        // TODO: Optional strict validation: Files written by LibreOffice have red-black violations.
#if false
        if (entry.Color is NodeColor.Red && sibling.Color is NodeColor.Red)
            throw new FileFormatException("Red-black tree red-violation.");
#endif
    }

    public static void ThrowIfEnumerationNotStarted(bool started)
    {
        if (!started)
            throw new InvalidOperationException("Enumeration has not started. Call MoveNext.");
    }

    public static void ThrowIfDirectoryEntryStreamIdIsInvalid(uint streamId)
    {
        if (streamId > StreamId.Maximum)
            throw new FileFormatException($"Invalid directory entry stream ID: {streamId:X8}.");
    }

    [DoesNotReturn]
    public static void ThrowDirectoryEntryNotFound(uint streamId)
        => throw new FileFormatException($"Directory entry {streamId} was not found.");
}
