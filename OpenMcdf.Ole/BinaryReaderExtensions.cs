using System.Text;

namespace OpenMcdf.Ole;

internal static class BinaryReaderExtensions
{
    // Read a CodePage string
    // This is a string whose length is specified as a number of bytes, and which is encoded in a specified codepage.
    // The CodePage *might* be CP_WINUNICODE, because LPWSTR properties have to be WINUNICODE, but WINUNICODE properties don't technically *have* to be LPWSTR
    public static string ReadCodePageString(this BinaryReader target, Encoding encoding)
    {
        // Read the length of the string, as a number of bytes.
        int byteLength = (int)target.ReadUInt32();
        return target.ReadStringWithEncoding(byteLength, encoding);
    }

    // Read an LPWSTR type property
    // This is a string which is encoded in the CP_WINUNICODE codepage and whose length is specific as a number of 2-byte characters.
    public static string ReadLpwstr(this BinaryReader target)
    {
        int nChars = (int)target.ReadUInt32();

        // The length is specified as a number of 2-byte characters, so the number of bytes to read is twice that.
        return target.ReadStringWithEncoding(nChars * 2, Encoding.Unicode);
    }

    // Common Implementation of the above 2 functions, just working off byte lengths and the specified encoding
    private static string ReadStringWithEncoding(this BinaryReader target, int byteLength, Encoding encoding)
    {
        if (byteLength == 0)
        {
            return string.Empty;
        }

        // Read the data
        byte[] stringBytes = target.ReadBytes(byteLength);

        // Skip the null terminator and convert the rest of the data
        int nullByteCount = encoding.CodePage == CodePages.WinUnicode ? 2 : 1;
        int nonNullSize = Math.Max(0, stringBytes.Length - nullByteCount);

        return encoding.GetString(stringBytes, 0, nonNullSize);
    }
}
