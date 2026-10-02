namespace OpenMcdf;

/// <summary>
/// Stream ID constants for <see cref="DirectoryEntry"/>.
/// </summary>
internal static class StreamId
{
    public const uint Root = 0;
    public const uint Maximum = 0xFFFFFFFA;
    public const uint NoStream = 0xFFFFFFFF;

    public static bool IsValid(uint value) => value is <= Maximum or NoStream;

    // EndOfChain is not technically valid according to the CFB specification, but it is used in practice by the
    // reference implementation for directory entry start sectors and storage entries in Wine.
    public static bool IsValidInPractice(uint value) => value is <= Maximum or NoStream or SectorType.EndOfChain;
}
