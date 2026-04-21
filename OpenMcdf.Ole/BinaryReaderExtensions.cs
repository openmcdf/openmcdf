namespace OpenMcdf.Ole;

internal static class BinaryReaderExtensions
{
#if !NET10_0_OR_GREATER
    public static void ReadExactly(this BinaryReader target, Span<byte> buffer) => target.BaseStream.ReadExactly(buffer);
#endif

    public static void SkipPadding(this BinaryReader br, int count)
    {
        if (count > 0)
        {
            Span<byte> localBuffer = stackalloc byte[count];
            br.ReadExactly(localBuffer);
        }
    }
}
