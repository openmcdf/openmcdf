using System.Text;

namespace OpenMcdf.Ole;

internal sealed class DictionaryProperty : IProperty
{
    private readonly int codePage;
    private Dictionary<uint, string>? entries = new();

    public DictionaryProperty(int codePage)
    {
        this.codePage = codePage;
    }

    public PropertyType PropertyType => PropertyType.DictionaryProperty;

    public object? Value
    {
        get => entries;
        set => entries = (Dictionary<uint, string>?)value;
    }

    public void Read(BinaryReader br)
    {
        long curPos = br.BaseStream.Position;

        uint numEntries = br.ReadUInt32();

        // Encoding.GetEncoding can actually be quite slow, so as all strings are in the same codepage, get the encoding once and then use it for each property.
        Encoding encoding = Encoding.GetEncoding(codePage);

        for (uint i = 0; i < numEntries; i++)
        {
            ReadEntry(br, encoding);
        }

        SkipPadding(br, curPos);
    }

    // Read a single dictionary entry
    private void ReadEntry(BinaryReader br, Encoding encoding)
    {
        uint propertyIdentifier = br.ReadUInt32();

        long curPos = br.BaseStream.Position;

        string entryName = encoding.CodePage == CodePages.WinUnicode ? br.ReadLpwstr() : br.ReadCodePageString(encoding);

        // WinUnicode strings are padded and we have to skip the padding. Other encodings are unpadded.
        if (encoding.CodePage == CodePages.WinUnicode)
        {
            SkipPadding(br, curPos);
        }

        entries!.Add(propertyIdentifier, entryName);
    }

    /// <summary>
    /// Write the dictionary and all its values into the specified <see cref="BinaryWriter"/>.
    /// </summary>
    /// <remarks>
    /// Based on the Microsoft specifications at https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-oleps/99127b7f-c440-4697-91a4-c853086d6b33.
    /// </remarks>
    /// <param name="bw">A writer to write the dictionary into.</param>
    public void Write(BinaryWriter bw)
    {
        long curPos = bw.BaseStream.Position;

        bw.Write(entries!.Count);

        // Encoding.GetEncoding can actually be quite slow, so as all strings are in the same codepage, get the encoding once and then use it for each property.
        Encoding encoding = Encoding.GetEncoding(codePage);

        foreach (KeyValuePair<uint, string> kv in entries)
        {
            WriteEntry(bw, kv.Key, kv.Value, encoding);
        }

        int size = (int)(bw.BaseStream.Position - curPos);
        WritePaddingIfNeeded(bw, size);
    }

    // Write a single entry to the dictionary, and handle and required null termination and padding.
    private void WriteEntry(BinaryWriter bw, uint propertyIdentifier, string name, Encoding encoding)
    {
        // Write the PropertyIdentifier
        bw.Write(propertyIdentifier);

        // If the code page is WINUNICODE, write the length as the number of characters and pad the length to a multiple of 4 bytes
        // Otherwise, write the length as the number of bytes and don't pad.
        if (codePage == CodePages.WinUnicode)
        {
            long curPos = bw.BaseStream.Position;

            bw.WriteLpwstr(name);

            int size = (int)(bw.BaseStream.Position - curPos);
            WritePaddingIfNeeded(bw, size);
        }
        else
        {
            bw.WriteCodePageString(name, encoding);
        }
    }

    // Write as much padding as needed to pad fieldLength to a multiple of 4 bytes
    private static void WritePaddingIfNeeded(BinaryWriter bw, int fieldLength)
    {
        int m = fieldLength % 4;

        if (m > 0)
        {
            for (int i = 0; i < 4 - m; i++) // padding
                bw.Write((byte)0);
        }
    }

    // Skip padding up to the 4 byte alignment, if needed
    private static void SkipPadding(BinaryReader br, long startPos)
    {
        int m = (int)(br.BaseStream.Position - startPos) % 4;

        if (m > 0)
        {
            for (int i = 0; i < m; i++)
            {
                br.ReadByte();
            }
        }
    }
}
