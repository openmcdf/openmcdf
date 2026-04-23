namespace OpenMcdf.Ole;

internal sealed class PropertySetStream
{
    private sealed class OffsetContainer
    {
        public int OffsetPS { get; set; }

        public List<long> PropertyIdentifierOffsets { get; } = new();

        public List<long> PropertyOffsets { get; } = new();
    }

    public ushort ByteOrder { get; set; }

    public ushort Version { get; set; }

    public uint SystemIdentifier { get; set; }

    public Guid CLSID { get; set; }

    public uint NumPropertySets { get; set; }

    public Guid FMTID0 { get; set; }

    public uint Offset0 { get; set; }

    public Guid FMTID1 { get; set; }

    public uint Offset1 { get; set; }

    public PropertySet? PropertySet0 { get; set; }

    public PropertySet? PropertySet1 { get; set; }

    public PropertySetStream()
    {
    }

    public void Read(BinaryReader br)
    {
        br.BaseStream.Position = 0;

        ByteOrder = br.ReadUInt16();
        Version = br.ReadUInt16();
        SystemIdentifier = br.ReadUInt32();
        CLSID = br.ReadGuid();
        NumPropertySets = br.ReadUInt32();
        FMTID0 = br.ReadGuid();
        Offset0 = br.ReadUInt32();

        if (NumPropertySets == 2)
        {
            FMTID1 = br.ReadGuid();
            Offset1 = br.ReadUInt32();
        }

        uint size = br.ReadUInt32();
        uint propertyCount = br.ReadUInt32();
        PropertySet0 = new PropertySet
        {
            Size = size,
        };

        // Create appropriate property factory based on the stream type
        PropertyFactory factory = FMTID0 == FormatIdentifiers.DocSummaryInformation ? DocumentSummaryInfoPropertyFactory.Default : DefaultPropertyFactory.Default;

        // Read property offsets (P0)
        for (int i = 0; i < propertyCount; i++)
        {
            PropertyIdentifierAndOffset pio = new();
            pio.Read(br);
            PropertySet0.PropertyIdentifierAndOffsets.Add(pio);
        }

        PropertySet0.LoadContext((int)Offset0, br);  // Read CodePage, Locale

        // Read properties (P0)
        for (int i = 0; i < propertyCount; i++)
        {
            PropertyIdentifierAndOffset propertyIdentifierAndOffset = PropertySet0.PropertyIdentifierAndOffsets[i];
            br.BaseStream.Seek(Offset0 + propertyIdentifierAndOffset.Offset, SeekOrigin.Begin);
            IProperty property = ReadProperty(propertyIdentifierAndOffset.PropertyIdentifier, PropertySet0.PropertyContext.CodePage, br, factory);
            PropertySet0.Properties.Add(property);
        }

        if (NumPropertySets == 2)
        {
            br.BaseStream.Seek(Offset1, SeekOrigin.Begin);

            size = br.ReadUInt32();
            propertyCount = br.ReadUInt32();

            PropertySet1 = new PropertySet
            {
                Size = size,
            };

            // Read property offsets
            for (int i = 0; i < propertyCount; i++)
            {
                PropertyIdentifierAndOffset pio = new();
                pio.Read(br);
                PropertySet1.PropertyIdentifierAndOffsets.Add(pio);
            }

            PropertySet1.LoadContext((int)Offset1, br);

            // Read properties
            for (int i = 0; i < propertyCount; i++)
            {
                PropertyIdentifierAndOffset idAndOffset = PropertySet1.PropertyIdentifierAndOffsets[i];
                br.BaseStream.Seek(Offset1 + idAndOffset.Offset, SeekOrigin.Begin);
                IProperty property = ReadProperty(idAndOffset.PropertyIdentifier, PropertySet1.PropertyContext.CodePage, br, DefaultPropertyFactory.Default);
                PropertySet1.Properties.Add(property);
            }
        }
    }

    public void Write(BinaryWriter bw)
    {
        bw.BaseStream.Position = 0;

        OffsetContainer oc0 = new();
        OffsetContainer oc1 = new();

        bw.Write(ByteOrder);
        bw.Write(Version);
        bw.Write(SystemIdentifier);
        bw.Write(CLSID.ToByteArray());
        bw.Write(NumPropertySets);
        bw.Write(FMTID0.ToByteArray());
        bw.Write(Offset0);

        if (NumPropertySets == 2)
        {
            bw.Write(FMTID1.ToByteArray());
            bw.Write(Offset1);
        }

        oc0.OffsetPS = (int)bw.BaseStream.Position;
        bw.Write(PropertySet0!.Size);
        bw.Write(PropertySet0.Properties.Count);

        // w property offsets
        for (int i = 0; i < PropertySet0.Properties.Count; i++)
        {
            oc0.PropertyIdentifierOffsets.Add(bw.BaseStream.Position); // Offset of 4 to Offset value
            PropertySet0.PropertyIdentifierAndOffsets[i].Write(bw);
        }

        for (int i = 0; i < PropertySet0.Properties.Count; i++)
        {
            oc0.PropertyOffsets.Add(bw.BaseStream.Position);
            PropertySet0.Properties[i].Write(bw);
        }

        long padding0 = bw.BaseStream.Position % 4;

        if (padding0 > 0)
        {
            for (int p = 0; p < 4 - padding0; p++)
                bw.Write((byte)0);
        }

        int size0 = (int)(bw.BaseStream.Position - oc0.OffsetPS);

        if (NumPropertySets == 2)
        {
            oc1.OffsetPS = (int)bw.BaseStream.Position;

            bw.Write(PropertySet1!.Size);
            bw.Write(PropertySet1.Properties.Count);

            // w property offsets
            for (int i = 0; i < PropertySet1.PropertyIdentifierAndOffsets.Count; i++)
            {
                oc1.PropertyIdentifierOffsets.Add(bw.BaseStream.Position); // Offset of 4 to Offset value
                PropertySet1.PropertyIdentifierAndOffsets[i].Write(bw);
            }

            for (int i = 0; i < PropertySet1.Properties.Count; i++)
            {
                oc1.PropertyOffsets.Add(bw.BaseStream.Position);
                PropertySet1.Properties[i].Write(bw);
            }

            int size1 = (int)(bw.BaseStream.Position - oc1.OffsetPS);

            bw.Seek(oc1.OffsetPS, SeekOrigin.Begin);
            bw.Write(size1);
        }

        bw.Seek(oc0.OffsetPS, SeekOrigin.Begin);
        bw.Write(size0);

        int shiftO1 = 2 + 2 + 4 + 16 + 4 + 16; // OFFSET0
        bw.Seek(shiftO1, SeekOrigin.Begin);
        bw.Write(oc0.OffsetPS);

        if (NumPropertySets == 2)
        {
            bw.Seek(shiftO1 + 4 + 16, SeekOrigin.Begin);
            bw.Write(oc1.OffsetPS);
        }

        //-----------
        for (int i = 0; i < PropertySet0.PropertyIdentifierAndOffsets.Count; i++)
        {
            bw.Seek((int)oc0.PropertyIdentifierOffsets[i] + 4, SeekOrigin.Begin); // Offset of 4 to Offset value
            bw.Write((int)(oc0.PropertyOffsets[i] - oc0.OffsetPS));
        }

        if (PropertySet1 is not null)
        {
            for (int i = 0; i < PropertySet1.PropertyIdentifierAndOffsets.Count; i++)
            {
                bw.Seek((int)oc1.PropertyIdentifierOffsets[i] + 4, SeekOrigin.Begin); // Offset of 4 to Offset value
                bw.Write((int)(oc1.PropertyOffsets[i] - oc1.OffsetPS));
            }
        }
    }

    private static IProperty ReadProperty(uint propertyIdentifier, int codePage, BinaryReader br, PropertyFactory factory)
    {
        if (propertyIdentifier == SpecialPropertyIdentifiers.Dictionary)
        {
            DictionaryProperty dictionaryProperty = new(codePage);
            dictionaryProperty.Read(br);
            return dictionaryProperty;
        }

        var vType = (VTPropertyType)br.ReadUInt16();
        br.ReadUInt16(); // Ushort Padding

        ITypedPropertyValue pr = factory.CreateProperty(vType, codePage, propertyIdentifier);
        pr.Read(br);

        return pr;
    }
}
