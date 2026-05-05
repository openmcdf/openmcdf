using System.Text;

namespace OpenMcdf.Ole;

internal static class CodePages
{
    public const int WinUnicode = 0x04B0;
    public const int UTF8 = 65001;

    // Get the encoding for the specified codepage
    // Special case WinUnicode because it's special cases in general, and it's faster to get it directly than to call GetEndoding.
    // @@TODO@@ Is it worth special caseing UTF-8 as well?
    internal static Encoding GetEncodingForCodePage(int codePage)
    {
        return codePage switch
        {
            WinUnicode => Encoding.Unicode,
            UTF8 => Encoding.UTF8,
            _ => Encoding.GetEncoding(codePage),
        };
    }
}
