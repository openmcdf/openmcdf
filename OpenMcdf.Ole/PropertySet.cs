namespace OpenMcdf.Ole;

internal sealed class PropertySet
{
    public PropertyContext PropertyContext { get; set; } = new();

    public uint Size { get; set; }

    public List<PropertyIdentifierAndOffset> PropertyIdentifierAndOffsets { get; } = new();

    public List<IProperty> Properties { get; } = new();

    public void LoadContext(int propertySetOffset, BinaryReader br)
    {
        long currPos = br.BaseStream.Position;

        // Read the code page - this should always be present
        int codePagePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.CodePage);
        if (codePagePropertyIndex == -1)
        {
            throw new FileFormatException("Required CodePage property not present");
        }

        PropertyIdentifierAndOffset codePageProperty = PropertyIdentifierAndOffsets[codePagePropertyIndex];
        long codePageOffset = propertySetOffset + codePageProperty.Offset;
        br.BaseStream.Seek(codePageOffset, SeekOrigin.Begin);

        var vType = (VTPropertyType)br.ReadUInt16();
        br.ReadUInt16(); // Ushort Padding
        PropertyContext.CodePage = (ushort)br.ReadInt16();

        // Read the Locale, if present
        int localePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.Locale);
        if (localePropertyIndex != -1)
        {
            PropertyIdentifierAndOffset localeProperty = PropertyIdentifierAndOffsets[localePropertyIndex];
            long localeOffset = propertySetOffset + localeProperty.Offset;
            br.BaseStream.Seek(localeOffset, SeekOrigin.Begin);

            vType = (VTPropertyType)br.ReadUInt16();
            br.ReadUInt16(); // Ushort Padding
            PropertyContext.Locale = br.ReadUInt32();
        }

        br.BaseStream.Position = currPos;
    }

    public void Add(IDictionary<uint, string> propertyNames)
    {
        DictionaryProperty dictionaryProperty = new(PropertyContext.CodePage, CodePages.GetEncodingForCodePage(PropertyContext.CodePage))
        {
            Value = propertyNames,
        };
        Properties.Add(dictionaryProperty);
        PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(SpecialPropertyIdentifiers.Dictionary, 0));
    }
}
