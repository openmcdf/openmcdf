using System.Text;

namespace OpenMcdf.Ole;

internal static class BinaryWriterExtensions
{
    // Write a CodePage string
    // This is a string whose length is specified as a number of bytes, and which is encoded in a specified codepage.
    // The CodePage *might* be CP_WINUNICODE, because LPWSTR properties have to be WINUNICODE, but WINUNICODE properties don't technically *have* to be LPWSTR
    public static void WriteCodePageString(this BinaryWriter target, string value, Encoding encoding) => target.WriteStringCore(value, encoding, false);

    // Write an LPWSTR type property
    // This is a string which is encoded in the CP_WINUNICODE codepage and whose length is specific as a number of 2-byte characters.
    public static void WriteLpwstr(this BinaryWriter target, string value) => target.WriteStringCore(value, Encoding.Unicode, true);

    // Shared implementation for the above two functions
    // 'writeCharLength' is because there are some contexts where the written length is the number of bytes, and some where its the number of characters
    private static void WriteStringCore(this BinaryWriter target, string value, Encoding encoding, bool writeCharLength)
    {
        // If the string is empty, write a length of zero and do no more
        if (string.IsNullOrEmpty(value))
        {
            target.Write(0U);
        }
        else
        {
            // Encode string data with the current code page
            int codePage = encoding.CodePage;
            byte[] valueBytes = encoding.GetBytes(value);
            uint byteLength = (uint)valueBytes.Length;

            // Add the number of null terminator bytes - two for CP_WINUNICODE strings, 1 otherwise
            byteLength += codePage == CodePages.WinUnicode ? 2u : 1u;

            // Write either number of bytes or number of two byte characters
            target.Write(writeCharLength ? (byteLength / 2) : byteLength);
            target.Write(valueBytes);

            // Write two null bytes for Unicode strings, 1 otherwise
            target.Write((byte)0);

            if (codePage == CodePages.WinUnicode)
            {
                target.Write((byte)0);
            }
        }
    }
}
