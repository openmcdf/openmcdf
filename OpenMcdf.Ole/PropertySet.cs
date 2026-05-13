namespace OpenMcdf.Ole;

internal sealed class PropertySet
{
    public PropertyContext PropertyContext { get; set; } = new();

    public uint Size { get; set; }

    public List<PropertyIdentifierAndOffset> PropertyIdentifierAndOffsets { get; } = new();

    public List<IProperty> Properties { get; } = new();

    public void LoadContext(int propertySetOffset, BinaryReader br, Guid fmtID)
    {
        long currPos = br.BaseStream.Position;

        // Read the code page
        // The 'HwpSummaryInformation' stream doesn't have to contain a code page, but we treat it as mandatory for all other streams
        int codePagePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.CodePage);
        if (codePagePropertyIndex == -1)
        {
            // For HWP streams, treat the default code page as UTF-8
            // NOTE: This is what various other HWP readers do, but it needs more test files to confirm - it's common to use VT_LPWSTR properties which are always CP_WINUNICODE
            if (fmtID == FormatIdentifiers.HwpSummaryInformation)
            {
                PropertyContext.CodePage = 65001;
            }
            else
            {
                throw new FileFormatException("Required CodePage property not present");
            }
        }
        else
        {
            PropertyIdentifierAndOffset codePageProperty = PropertyIdentifierAndOffsets[codePagePropertyIndex];
            long codePageOffset = propertySetOffset + codePageProperty.Offset;
            br.BaseStream.Seek(codePageOffset, SeekOrigin.Begin);

            var vType = (VTPropertyType)br.ReadUInt16();
            br.ReadUInt16(); // Ushort Padding
            PropertyContext.CodePage = (ushort)br.ReadInt16();
        }

        // Read the Locale, if present
        int localePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.Locale);
        if (localePropertyIndex != -1)
        {
            PropertyIdentifierAndOffset localeProperty = PropertyIdentifierAndOffsets[localePropertyIndex];
            long localeOffset = propertySetOffset + localeProperty.Offset;
            br.BaseStream.Seek(localeOffset, SeekOrigin.Begin);

            var vType = (VTPropertyType)br.ReadUInt16();
            br.ReadUInt16(); // Ushort Padding
            PropertyContext.Locale = br.ReadUInt32();
        }

        br.BaseStream.Position = currPos;
    }

    public void Add(Dictionary<uint, string> propertyNames)
    {
        DictionaryProperty dictionaryProperty = new(PropertyContext.CodePage, propertyNames);
        Properties.Add(dictionaryProperty);
        PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(SpecialPropertyIdentifiers.Dictionary, 0));
    }
}
