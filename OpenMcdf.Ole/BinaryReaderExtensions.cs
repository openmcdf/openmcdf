using System.Text;

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

    public static Guid ReadGuid(this BinaryReader br)
    {
#if NETSTANDARD2_0
        byte[] localBuffer = new byte[16];
        br.ReadExactly(localBuffer);
        return new Guid(localBuffer);
#else
        Span<byte> localBuffer = stackalloc byte[16];
        br.ReadExactly(localBuffer);
        return new Guid(localBuffer);
#endif
    }

    // Read a null terminated string from the specified BinaryReader, using the specified length and codepage
    // Note: Encoding.GetEncoding seems to actually be quite slow, so allow callers that already have the Encoding to provide it directly.
    public static string ReadNullTerminatedStringWithEncoding(this BinaryReader target, int byteLength, int codePage, Encoding encoding)
    {
#if NETSTANDARD2_0
        byte[] nameBytes = new byte[byteLength];
        target.ReadExactly(nameBytes.AsSpan());
#else
        // @@TBD@@ What max stack alloc size should be used here?
        // Office limits many properties to 255 characters (e.g see the list in https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-oshared/3394ba97-9ea3-4b52-ba45-555bb5b8e94c) so we could use
        // that, but mostly they're much smaller so maybe that's overkill.
        Span<byte> nameBytes = byteLength <= 64 ? stackalloc byte[byteLength] : new byte[byteLength];
        target.ReadExactly(nameBytes);
#endif

        int nullByteCount = codePage == CodePages.WinUnicode ? 2 : 1;
        int valueSize = Math.Max(0, nameBytes.Length - nullByteCount); // Only convert the actual characters, not the null terminator

#if NETSTANDARD2_0
        return encoding.GetString(nameBytes, 0, valueSize);
#else
        return encoding.GetString(nameBytes[..valueSize]);
#endif
    }

    public static string ReadNullTerminatedString(this BinaryReader target, int byteLength, int codePage)
    {
        Encoding encoding = codePage == CodePages.WinUnicode ? Encoding.Unicode : Encoding.GetEncoding(codePage);
        return target.ReadNullTerminatedStringWithEncoding(byteLength, codePage, encoding);
    }

    public static string ReadNullTerminatedWideString(this BinaryReader target, int characterLength) => target.ReadNullTerminatedStringWithEncoding(byteLength: characterLength * 2, CodePages.WinUnicode, Encoding.Unicode);
}
