namespace OpenMcdf.Ole;

internal class PropertySet
{
    public PropertyContext PropertyContext { get; } = new();

    public uint Size { get; set; }

    public List<PropertyIdentifierAndOffset> PropertyIdentifierAndOffsets { get; }

    public List<IProperty> Properties { get; }

    protected virtual PropertyFactory PropertyFactory { get; } = DefaultPropertyFactory.Default;

    public DictionaryProperty? DictionaryProperty { get; }

    // Create a PropertySet by reading from the supplied BinaryReader
    public PropertySet(BinaryReader br, uint propertySetOffset)
    {
        this.Size = br.ReadUInt32();

        uint propertyCount = br.ReadUInt32();

        // Read property offsets
        // When reserving space in the collection, clamp the size in case it's a corrupt (i.e. huge) value. Value picked based on the number of properties in common property sets.
        this.PropertyIdentifierAndOffsets = new((int)Math.Min(propertyCount, 28));
        for (int i = 0; i < propertyCount; i++)
        {
            PropertyIdentifierAndOffset pio = PropertyIdentifierAndOffset.Read(br);
            PropertyIdentifierAndOffsets.Add(pio);
        }

        // Treat the code page property specially - we need it to read all the code page specific properties.
        this.PropertyContext.CodePage = ReadCodePage(br, propertySetOffset);

        // Read properties
        this.Properties = new(this.PropertyIdentifierAndOffsets.Count);
        for (int i = 0; i < propertyCount; i++)
        {
            PropertyIdentifierAndOffset propertyIdentifierAndOffset = this.PropertyIdentifierAndOffsets[i];
            br.BaseStream.Seek(propertySetOffset + propertyIdentifierAndOffset.Offset, SeekOrigin.Begin);
            IProperty property = ReadProperty(propertyIdentifierAndOffset.PropertyIdentifier, this.PropertyContext.CodePage, br);
            this.Properties.Add(property);

            if (property is DictionaryProperty dictionaryProperty)
                this.DictionaryProperty = dictionaryProperty;
        }

        // Load additional context properties
        LoadContext();
    }

    public PropertySet(PropertyContext propertyContext, int initialPropertyCount, Dictionary<uint, string>? propertyNames)
    {
        this.PropertyContext = propertyContext;
        this.PropertyIdentifierAndOffsets = new(initialPropertyCount);
        this.Properties = new(initialPropertyCount);

        if (propertyNames is not null)
        {
            this.DictionaryProperty = new(PropertyContext.CodePage, propertyNames);
            Properties.Add(this.DictionaryProperty);
            PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(SpecialPropertyIdentifiers.Dictionary, 0));
        }
    }

    public static PropertySet Read(in Guid fmtId, BinaryReader br, uint propertySetOffset)
    {
        if (fmtId == FormatIdentifiers.DocSummaryInformation)
            return new DocumentSummaryInformationPropertySet(br, propertySetOffset);

        if (fmtId == FormatIdentifiers.HwpSummaryInformation)
            return new HwpSummaryInformationPropertySet(br, propertySetOffset);

        return new PropertySet(br, propertySetOffset);
    }

    public static PropertySet Create(in Guid fmtId, PropertyContext propertyContext, int initialPropertyCount, Dictionary<uint, string>? propertyNames)
    {
        if (fmtId == FormatIdentifiers.DocSummaryInformation)
            return new DocumentSummaryInformationPropertySet(propertyContext, initialPropertyCount, propertyNames);

        if (fmtId == FormatIdentifiers.HwpSummaryInformation)
            return new HwpSummaryInformationPropertySet(propertyContext, initialPropertyCount, propertyNames);

        return new PropertySet(propertyContext, initialPropertyCount, propertyNames);
    }

    // ReadCodePage is virtual to allow PropertySet specific handling of missing/default values
    protected virtual int ReadCodePage(BinaryReader br, uint propertySetOffset)
    {
        int? propertySetCodePage = TryReadCodePage(br, propertySetOffset);
        return propertySetCodePage ?? throw new FileFormatException("Required CodePage property not present.");
    }

    protected int? TryReadCodePage(BinaryReader br, uint propertySetOffset)
    {
        int codePagePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.CodePage);
        if (codePagePropertyIndex == -1)
        {
            return null;
        }

        long codePageOffset = propertySetOffset + PropertyIdentifierAndOffsets[codePagePropertyIndex].Offset;
        br.BaseStream.Seek(codePageOffset, SeekOrigin.Begin);

        var vType = (VTPropertyType)br.ReadUInt16();
        br.ReadUInt16(); // Ushort Padding

        return (ushort)br.ReadInt16();
    }

    // Populate additional context properties, if present
    private void LoadContext()
    {
        // Read the Locale, if present
        int localePropertyIndex = PropertyIdentifierAndOffsets.FindIndex(static pio => pio.PropertyIdentifier == SpecialPropertyIdentifiers.Locale);
        if (localePropertyIndex != -1)
        {
            IProperty localeProperty = Properties[localePropertyIndex];
            if (localeProperty is ITypedPropertyValue { VTType: VTPropertyType.VT_UI4, Value: uint uintLocaleValue })
            {
                this.PropertyContext.Locale = uintLocaleValue;
            }
        }
    }

    public void AddProperty(VTPropertyType vType, uint propertyIdentifier, object? value)
    {
        ITypedPropertyValue p = this.PropertyFactory.CreateProperty(vType, PropertyContext.CodePage, propertyIdentifier);
        p.Value = value;
        this.Properties.Add(p);
        this.PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(propertyIdentifier, 0));
    }

    // Read a given property, special casing the dictionary property.
    // Note: This is virtual so that the behavior can be overridden in specific property sets
    protected virtual IProperty ReadProperty(uint propertyIdentifier, int codePage, BinaryReader br)
    {
        return propertyIdentifier == SpecialPropertyIdentifiers.Dictionary
            ? DictionaryProperty.Read(br, codePage)
            : ReadTypedProperty(propertyIdentifier, codePage, br);
    }

    // Read the specified typed property value
    protected ITypedPropertyValue ReadTypedProperty(uint propertyIdentifier, int codePage, BinaryReader br)
    {
        var vType = (VTPropertyType)br.ReadUInt16();
        br.ReadUInt16(); // Ushort Padding

        return this.PropertyFactory.ReadProperty(br, vType, codePage, propertyIdentifier);
    }
}

// Specific handling for the DocumentSummaryInformation property set - it has special handling for some unaligned strings
internal sealed class DocumentSummaryInformationPropertySet : PropertySet
{
    public DocumentSummaryInformationPropertySet(BinaryReader br, uint propertySetOffset)
        : base(br, propertySetOffset)
    {
    }

    public DocumentSummaryInformationPropertySet(PropertyContext propertyContext, int initialPropertyCount, Dictionary<uint, string>? propertyName)
        : base(propertyContext, initialPropertyCount, propertyName)
    {
    }

    protected override PropertyFactory PropertyFactory { get; } = DocumentSummaryInfoPropertyFactory.Default;
}

// Specific handling for the HwpSummaryInformation property set - It doesn't usually contain a codePage property and there is a property with an id of 0 that isn't a dictionary.
internal sealed class HwpSummaryInformationPropertySet : PropertySet
{
    public HwpSummaryInformationPropertySet(BinaryReader br, uint propertySetOffset)
        : base(br, propertySetOffset)
    {
    }

    public HwpSummaryInformationPropertySet(PropertyContext propertyContext, int initialPropertyCount, Dictionary<uint, string>? propertyNames)
        : base(propertyContext, initialPropertyCount, propertyNames)
    {
    }

    // The 'HwpSummaryInformation' stream doesn't have to contain a code page, treat the default codepage as UTF-8
    // NOTE: This is what various other HWP readers do, but it needs more test files to confirm - it's common to use VT_LPWSTR properties which are always CP_WINUNICODE
    protected override int ReadCodePage(BinaryReader br, uint propertySetOffset) => TryReadCodePage(br, propertySetOffset) ?? 65001;

    // Only support typed properties here, don't try to handle dictionary properties.
    protected override IProperty ReadProperty(uint propertyIdentifier, int codePage, BinaryReader br) => ReadTypedProperty(propertyIdentifier, codePage, br);
}
