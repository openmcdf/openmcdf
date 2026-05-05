using System.Text;

namespace OpenMcdf.Ole;

public enum ContainerType
{
    AppSpecific = 0,
    SummaryInfo = 1,
    DocumentSummaryInfo = 2,
    UserDefinedProperties = 3,
    GlobalInfo = 4,
    ImageContents = 5,
    ImageInfo = 6,
    HwpSummaryInfo = 7,
}

public class OlePropertiesContainer
{
    public Dictionary<uint, string>? PropertyNames { get; private set; }

    public OlePropertiesContainer? UserDefinedProperties { get; private set; }

    /// <summary>
    /// Gets the type of the container.
    /// </summary>
    public ContainerType ContainerType { get; }

    /// <summary>
    /// Gets the FMTID of the properties container.
    /// </summary>
    public Guid FMTID0 { get; }

    public PropertyContext Context { get; }

    private readonly List<OleProperty> properties;

    /// <summary>
    /// Initializes a new instance of the <see cref="OlePropertiesContainer"/> class.
    /// </summary>
    /// <param name="codePage">The code page to use for the new container.</param>
    /// <param name="containerType">The type of the new container.</param>
    public OlePropertiesContainer(int codePage, ContainerType containerType)
    {
        Context = new PropertyContext
        {
            CodePage = codePage,
            Behavior = Behavior.CaseInsensitive,
        };

        ContainerType = containerType;
        FMTID0 = FmtIdFromContainerType(containerType);
        properties = [];
    }

    public OlePropertiesContainer(CfbStream cfStream)
    {
        PropertySetStream pStream = new();

        using BinaryReader reader = new(cfStream, Encoding.Unicode, true);
        pStream.Read(reader);

        FMTID0 = pStream.FMTID0;
        ContainerType = ContainerTypeFromFmtId(pStream.FMTID0);

        PropertyNames = (Dictionary<uint, string>?)pStream.PropertySet0!.Properties
            .FirstOrDefault(p => p.PropertyType == PropertyType.DictionaryProperty)?.Value;

        Context = pStream.PropertySet0.PropertyContext;
        properties = new(pStream.PropertySet0.Properties.Count);

        for (int i = 0; i < pStream.PropertySet0.Properties.Count; i++)
        {
            PropertyIdentifierAndOffset propertyIdentifierAndOffset = pStream.PropertySet0.PropertyIdentifierAndOffsets[i];
            if (propertyIdentifierAndOffset.PropertyIdentifier == SpecialPropertyIdentifiers.Dictionary) continue;
            // if (propertyIdentifierAndOffset.PropertyIdentifier == 1) continue;
            // if (propertyIdentifierAndOffset.PropertyIdentifier == 0x80000000) continue;
            var p = (ITypedPropertyValue)pStream.PropertySet0.Properties[i];

            OleProperty op = new(this)
            {
                VTType = p.VTType,
                PropertyIdentifier = propertyIdentifierAndOffset.PropertyIdentifier,
                Value = p.Value,
            };

            properties.Add(op);
        }

        // Build the User Defined properties container, if the property set is present.
        if (pStream.NumPropertySets == 2)
        {
            UserDefinedProperties = new OlePropertiesContainer(pStream.PropertySet1!);
        }
    }

    // An extra private constructor used to build the UserDefinedProperties property set, if one is present in the file.
    private OlePropertiesContainer(PropertySet propertySet)
    {
        ContainerType = ContainerType.UserDefinedProperties;
        Context = propertySet.PropertyContext;
        FMTID0 = FormatIdentifiers.UserDefinedProperties;
        properties = new(propertySet.Properties.Count);

        for (int i = 0; i < propertySet.Properties.Count; i++)
        {
            PropertyIdentifierAndOffset propertyIdentifierAndOffset = propertySet.PropertyIdentifierAndOffsets[i];
            if (propertyIdentifierAndOffset.PropertyIdentifier is SpecialPropertyIdentifiers.Dictionary or SpecialPropertyIdentifiers.Locale)
                continue;

            var p = (ITypedPropertyValue)propertySet.Properties[i];

            OleProperty op = new(this)
            {
                VTType = p.VTType,
                PropertyIdentifier = propertyIdentifierAndOffset.PropertyIdentifier,
                Value = p.Value,
            };

            properties.Add(op);
        }

        var existingPropertyNames = (Dictionary<uint, string>?)propertySet.Properties
            .FirstOrDefault(p => p.PropertyType == PropertyType.DictionaryProperty)?.Value;

        PropertyNames = existingPropertyNames ?? [];
    }

    public IList<OleProperty> Properties => properties;

    public OleProperty CreateProperty(VTPropertyType vtPropertyType, uint propertyIdentifier, string? propertyName = null)
    {
        OleProperty op = new(this)
        {
            VTType = vtPropertyType,
            PropertyIdentifier = propertyIdentifier,
        };

        return op;
    }

    public void Add(OleProperty property) => properties.Add(property);

    /// <summary>
    /// Create a new UserDefinedProperty.
    /// </summary>
    /// <param name="vtPropertyType">The type of property to create.</param>
    /// <param name="name">The name of the new property.</param>
    /// <returns>The new property.</returns>
    /// <exception cref="InvalidOperationException">If UserDefinedProperties aren't allowed for this container.</exception>
    /// <exception cref="ArgumentException">If a property with the name <paramref name="name"/> already exists."/>.</exception>
    public OleProperty AddUserDefinedProperty(VTPropertyType vtPropertyType, string name)
    {
        if (this.ContainerType != ContainerType.UserDefinedProperties)
            throw new InvalidOperationException();

        PropertyNames ??= new();

        // As per https://learn.microsoft.com/en-us/openspecs/windows_protocols/MS-OLEPS/4177a4bc-5547-49fe-a4d9-4767350fd9cf
        // the property names have to be unique, and are case insensitive.
        if (PropertyNames.Any(property => property.Value.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
        {
            throw new ArgumentException($"User defined property names must be unique and {name} already exists.", nameof(name));
        }

        // Work out a property identifier - must be > 1 and unique as per
        // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-oleps/333959a3-a999-4eca-8627-48a224e63e77
        uint identifier = 2;

        if (PropertyNames.Count > 0)
        {
            uint highestIdentifier = PropertyNames.Keys.Max();
            identifier = Math.Max(highestIdentifier, 2) + 1;
        }

        PropertyNames[identifier] = name;

        var op = new OleProperty(this)
        {
            VTType = vtPropertyType,
            PropertyIdentifier = identifier,
        };

        properties.Add(op);

        return op;
    }

    public void RemoveProperty(uint propertyIdentifier)
    {
        OleProperty? toRemove = properties.FirstOrDefault(o => o.PropertyIdentifier == propertyIdentifier);

        if (toRemove is not null)
            properties.Remove(toRemove);
    }

    /// <summary>
    /// Create a new UserDefinedProperties container within this container.
    /// </summary>
    /// <remarks>
    /// Only containers of type DocumentSummaryInfo can contain user defined properties.
    /// </remarks>
    /// <param name="codePage">The code page to use for the user defined properties.</param>
    /// <returns>The UserDefinedProperties container.</returns>
    /// <exception cref="InvalidOperationException">If this container is a type that doesn't suppose user defined properties.</exception>
    public OlePropertiesContainer CreateUserDefinedProperties(int codePage)
    {
        // Only the DocumentSummaryInfo stream can contain a UserDefinedProperties
        if (ContainerType != ContainerType.DocumentSummaryInfo)
            throw new InvalidOperationException($"Only a DocumentSummaryInfo can contain user defined properties. Current container type is {ContainerType}");

        // Create the container, and add the code page to the initial set of properties
        UserDefinedProperties = new OlePropertiesContainer(codePage, ContainerType.UserDefinedProperties)
        {
            PropertyNames = new Dictionary<uint, string>(),
        };

        var op = new OleProperty(UserDefinedProperties)
        {
            VTType = VTPropertyType.VT_I2,
            PropertyIdentifier = 1,
            Value = (short)codePage,
        };

        UserDefinedProperties.properties.Add(op);

        return UserDefinedProperties;
    }

    public void Save(Stream cfStream)
    {
        using BinaryWriter bw = new(cfStream, Encoding.UTF8, leaveOpen: true);

        PropertySetStream ps = new()
        {
            ByteOrder = 0xFFFE,
            Version = 0,
            SystemIdentifier = 0x00020006,
            CLSID = Guid.Empty,

            NumPropertySets = 1,

            FMTID0 = this.FMTID0,
            Offset0 = 0,

            FMTID1 = Guid.Empty,
            Offset1 = 0,

            PropertySet0 = new PropertySet
            {
                PropertyContext = Context,
            },
        };

        // If we're writing an AppSpecific property set and have property names, then add a dictionary property
        if (ContainerType == ContainerType.AppSpecific && PropertyNames is not null && PropertyNames.Count > 0)
        {
            ps.PropertySet0.Add(PropertyNames);
        }

        PropertyFactory factory =
            ContainerType == ContainerType.DocumentSummaryInfo ? DocumentSummaryInfoPropertyFactory.Default : DefaultPropertyFactory.Default;

        Encoding propertySet0Encoding = CodePages.GetEncodingForCodePage(ps.PropertySet0.PropertyContext.CodePage);

        foreach (OleProperty op in Properties)
        {
            ITypedPropertyValue p = factory.CreateProperty(op.VTType, Context.CodePage, propertySet0Encoding, op.PropertyIdentifier);
            p.Value = op.Value;
            ps.PropertySet0.Properties.Add(p);
            ps.PropertySet0.PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(op.PropertyIdentifier, 0));
        }

        if (UserDefinedProperties is not null)
        {
            ps.NumPropertySets = 2;

            ps.PropertySet1 = new PropertySet
            {
                PropertyContext = UserDefinedProperties.Context,
            };

            ps.FMTID1 = FormatIdentifiers.UserDefinedProperties;
            ps.Offset1 = 0;

            // Add the dictionary containing the property names
            ps.PropertySet1.Add(UserDefinedProperties.PropertyNames!);

            Encoding propertySet1Encoding = CodePages.GetEncodingForCodePage(ps.PropertySet1.PropertyContext.CodePage);

            // Add the properties themselves
            foreach (OleProperty op in UserDefinedProperties.Properties)
            {
                ITypedPropertyValue p = DefaultPropertyFactory.Default.CreateProperty(op.VTType, ps.PropertySet1.PropertyContext.CodePage, propertySet1Encoding, op.PropertyIdentifier);
                p.Value = op.Value;
                ps.PropertySet1.Properties.Add(p);
                ps.PropertySet1.PropertyIdentifierAndOffsets.Add(new PropertyIdentifierAndOffset(op.PropertyIdentifier, 0));
            }
        }

        ps.Write(bw);
    }

    // Determine the type of the container from the FMTID0 property.
    private static ContainerType ContainerTypeFromFmtId(Guid fmtId0)
    {
        if (fmtId0 == FormatIdentifiers.SummaryInformation)
            return ContainerType.SummaryInfo;
        else if (fmtId0 == FormatIdentifiers.DocSummaryInformation)
            return ContainerType.DocumentSummaryInfo;
        else if (fmtId0 == FormatIdentifiers.GlobalInfo)
            return ContainerType.GlobalInfo;
        else if (fmtId0 == FormatIdentifiers.ImageInfo)
            return ContainerType.ImageInfo;
        else if (fmtId0 == FormatIdentifiers.ImageContents)
            return ContainerType.ImageContents;
        else if (fmtId0 == FormatIdentifiers.UserDefinedProperties)
            return ContainerType.UserDefinedProperties;
        else if (fmtId0 == FormatIdentifiers.HwpSummaryInformation)
            return ContainerType.HwpSummaryInfo;

        return ContainerType.AppSpecific;
    }

    // Determine the FMTID property from the container type.
    // Note: Uses FMTID_DocSummaryInformation by default to match the previous behavior.
    private static Guid FmtIdFromContainerType(ContainerType containerType)
    {
        return containerType switch
        {
            ContainerType.SummaryInfo => FormatIdentifiers.SummaryInformation,
            ContainerType.GlobalInfo => FormatIdentifiers.GlobalInfo,
            ContainerType.ImageContents => FormatIdentifiers.ImageContents,
            ContainerType.ImageInfo => FormatIdentifiers.ImageInfo,
            ContainerType.UserDefinedProperties => FormatIdentifiers.UserDefinedProperties,
            ContainerType.HwpSummaryInfo => FormatIdentifiers.HwpSummaryInformation,
            _ => FormatIdentifiers.DocSummaryInformation,
        };
    }
}
