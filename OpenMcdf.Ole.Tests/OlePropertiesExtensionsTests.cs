using OpenMcdf.Tests;
using System.Diagnostics;

namespace OpenMcdf.Ole.Tests;

[TestClass]
public class OlePropertiesExtensionsTests
{
    [TestMethod]
    public void ReadSummaryInformation()
    {
        using var cf = RootStorage.OpenRead("_Test.ppt");
        using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);
        OlePropertiesContainer co = new(stream);

        foreach (OleProperty p in co.Properties)
        {
            Debug.WriteLine(p);
        }
    }

    [TestMethod]
    public void ReadDocumentSummaryInformation()
    {
        using var cf = RootStorage.OpenRead("_Test.ppt");
        using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(stream);

        foreach (OleProperty p in co.Properties)
        {
            Debug.WriteLine(p);
        }
    }

    [TestMethod]
    public void ReadThenWriteDocumentSummaryInformation()
    {
        using var cf = RootStorage.OpenRead("_Test.ppt");
        using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(stream);

        using var cf2 = RootStorage.CreateInMemory();
        using CfbStream stream2 = cf2.CreateStream(PropertySetNames.DocSummaryInformation);
        co.Save(stream2);
    }

    // Modify some document summary information properties, save to a file, and then validate the expected results
    [TestMethod]
    public void ModifyDocumentSummaryInformation()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("_Test.ppt");

        // Verify initial properties, and then create a modified document
        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(dsiStream);

            // The company property should exist but be empty
            OleProperty companyProperty = co.Properties.First(prop => prop.PropertyName == "PIDDSI_COMPANY");
            Assert.AreEqual(string.Empty, companyProperty.Value);

            // As a sanity check, check that the value of a property that we don't change remains the same
            OleProperty formatProperty = co.Properties.First(prop => prop.PropertyName == "PIDDSI_PRESFORMAT");
            Assert.AreEqual("A4 Paper (210x297 mm)", formatProperty.Value);

            // The manager property shouldn't exist, and we'll add it
            Assert.IsFalse(co.Properties.Any(prop => prop.PropertyName == "PIDDSI_MANAGER"));

            OleProperty managerProp = co.CreateProperty(VTPropertyType.VT_LPSTR, 0x0000000E, "PIDDSI_MANAGER");
            co.Add(managerProp);

            companyProperty.Value = "My Company";
            managerProp.Value = "The Boss";

            co.Save(dsiStream);
        }

        using (var cf = RootStorage.Open(modifiedStream))
        {
            using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(stream);

            OleProperty companyProperty = co.Properties.First(prop => prop.PropertyName == "PIDDSI_COMPANY");
            Assert.AreEqual("My Company", companyProperty.Value);

            OleProperty formatProperty = co.Properties.First(prop => prop.PropertyName == "PIDDSI_PRESFORMAT");
            Assert.AreEqual("A4 Paper (210x297 mm)", formatProperty.Value);

            OleProperty managerProperty = co.Properties.First(prop => prop.PropertyName == "PIDDSI_MANAGER");
            Assert.AreEqual("The Boss", managerProperty.Value);
        }
    }

    [TestMethod]
    public void ReadSummaryInformationUtf8()
    {
        // Regression test for #33
        using var cf = RootStorage.OpenRead("wstr_presets.doc");
        using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);
        OlePropertiesContainer co = new(stream);

        foreach (OleProperty p in co.Properties)
        {
            Debug.WriteLine(p);
        }

        using CfbStream stream2 = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co2 = new(stream2);

        foreach (OleProperty p in co2.Properties)
        {
            Debug.WriteLine(p);
        }
    }

    [TestMethod]
    public void ReadSummaryInformationUtf8Part2()
    {
        // Regression test for #34
        using var cf = RootStorage.OpenRead("2custom.doc");
        using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);
        OlePropertiesContainer co = new(stream);

        foreach (OleProperty p in co.Properties)
        {
            Debug.WriteLine(p);
        }

        using CfbStream stream2 = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co2 = new(stream2);

        foreach (OleProperty p in co2.Properties)
        {
            Debug.WriteLine(p);
        }

        if (co2.UserDefinedProperties is not null)
        {
            foreach (OleProperty p in co2.UserDefinedProperties.Properties)
            {
                Debug.WriteLine(p);
            }
        }
    }

    [TestMethod]
    public void SummaryInformationReadLpwstring()
    {
        using var cf = RootStorage.OpenRead("english.presets.doc");
        using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);
        OlePropertiesContainer co = new(stream);

        foreach (OleProperty p in co.Properties)
        {
            Debug.WriteLine(p);
        }
    }

    // Test that we can modify an LPWSTR property, and the value is null terminated as required
    [TestMethod]
    public void SummaryInformationModifyLpwstring()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("wstr_presets.doc");

        // Modify some LPWSTR properties, and save to a new file
        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream dsiStream = cf.OpenStream(PropertySetNames.SummaryInformation);
            OlePropertiesContainer co = new(dsiStream);

            OleProperty authorProperty = co.Properties.First(prop => prop.PropertyName == "PIDSI_AUTHOR");
            Assert.AreEqual(VTPropertyType.VT_LPWSTR, authorProperty.VTType);
            Assert.AreEqual("zkyiqpqoroxnbdwhnjfqroxlgylpbgcwuhjfifpkvycugvuecoputqgknnbs", authorProperty.Value);

            OleProperty keyWordsProperty = co.Properties.First(prop => prop.PropertyName == "PIDSI_KEYWORDS");
            Assert.AreEqual(VTPropertyType.VT_LPWSTR, keyWordsProperty.VTType);
            Assert.AreEqual("abcdefghijk", keyWordsProperty.Value);

            authorProperty.Value = "ABC";
            keyWordsProperty.Value = string.Empty;
            co.Save(dsiStream);
        }

        // Open the new file and check for the expected values
        using (var cf = RootStorage.Open(modifiedStream))
        {
            using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);
            OlePropertiesContainer co = new(stream);

            OleProperty authorProperty = co.Properties.First(prop => prop.PropertyName == "PIDSI_AUTHOR");
            Assert.AreEqual(VTPropertyType.VT_LPWSTR, authorProperty.VTType);
            Assert.AreEqual("ABC", authorProperty.Value);

            OleProperty keyWordsProperty = co.Properties.First(prop => prop.PropertyName == "PIDSI_KEYWORDS");
            Assert.AreEqual(VTPropertyType.VT_LPWSTR, keyWordsProperty.VTType);
            Assert.AreEqual(string.Empty, keyWordsProperty.Value);
        }
    }

    // winUnicodeDictionary.doc contains a UserProperties section with the CP_WINUNICODE codepage, and LPWSTR string properties
    [TestMethod]
    public void TestReadUnicodeUserPropertiesDictionary()
    {
        using var cf = RootStorage.OpenRead("winUnicodeDictionary.doc");
        CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(dsiStream);
        OlePropertiesContainer? userProps = co.UserDefinedProperties;

        Assert.IsNotNull(userProps);

        // CodePage should be CP_WINUNICODE (1200)
        Assert.AreEqual(1200, userProps.Context.CodePage);

        // There should be 5 property names present, and 6 properties (the properties include the code page)
        Assert.IsNotNull(userProps.PropertyNames);
        Assert.HasCount(5, userProps.PropertyNames);
        Assert.HasCount(6, userProps.Properties);

        // Check for expected names and values
        OleProperty[] propArray = userProps.Properties.ToArray();

        // CodePage prop
        Assert.AreEqual(1u, propArray[0].PropertyIdentifier);
        Assert.AreEqual("0x00000001", propArray[0].PropertyName);
        Assert.AreEqual((short)1200, propArray[0].Value);

        // String properties
        Assert.AreEqual("A", propArray[1].PropertyName);
        Assert.AreEqual(string.Empty, propArray[1].Value);
        Assert.AreEqual("AB", propArray[2].PropertyName);
        Assert.AreEqual("X", propArray[2].Value);
        Assert.AreEqual("ABC", propArray[3].PropertyName);
        Assert.AreEqual("XY", propArray[3].Value);
        Assert.AreEqual("ABCD", propArray[4].PropertyName);
        Assert.AreEqual("XYZ", propArray[4].Value);
        Assert.AreEqual("ABCDE", propArray[5].PropertyName);
        Assert.AreEqual("XYZ!", propArray[5].Value);
    }

    // Test that we can add user properties of various types and then read them back
    [TestMethod]
    public void AddDocumentSummaryInformationCustomInfo()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("english.presets.doc");

        // Test value for a VT_FILETIME property
        DateTime testNow = DateTime.UtcNow;

        // english.presets.doc has a user defined property section, but no properties other than the codepage
        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(dsiStream);
            OlePropertiesContainer? userProperties = co.UserDefinedProperties;

            Assert.IsNotNull(userProperties?.PropertyNames);

            userProperties.PropertyNames[2] = "StringProperty";
            userProperties.PropertyNames[3] = "BooleanProperty";
            userProperties.PropertyNames[4] = "IntegerProperty";
            userProperties.PropertyNames[5] = "DateProperty";
            userProperties.PropertyNames[6] = "DoubleProperty";

            OleProperty stringProperty = co.CreateProperty(VTPropertyType.VT_LPSTR, 2);
            stringProperty.Value = "Hello";
            userProperties.Add(stringProperty);

            OleProperty booleanProperty = co.CreateProperty(VTPropertyType.VT_BOOL, 3);
            booleanProperty.Value = true;
            userProperties.Add(booleanProperty);

            OleProperty integerProperty = co.CreateProperty(VTPropertyType.VT_I4, 4);
            integerProperty.Value = 3456;
            userProperties.Add(integerProperty);

            OleProperty timeProperty = co.CreateProperty(VTPropertyType.VT_FILETIME, 5);
            timeProperty.Value = testNow;
            userProperties.Add(timeProperty);

            OleProperty doubleProperty = co.CreateProperty(VTPropertyType.VT_R8, 6);
            doubleProperty.Value = 1.234567d;
            userProperties.Add(doubleProperty);

            co.Save(dsiStream);
        }

        using (var cf = RootStorage.Open(modifiedStream))
        {
            using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(stream);

            Assert.IsNotNull(co.UserDefinedProperties);
            OleProperty[] propArray = co.UserDefinedProperties.Properties.ToArray();
            Assert.HasCount(6, propArray);

            // CodePage prop
            Assert.AreEqual(1u, propArray[0].PropertyIdentifier);
            Assert.AreEqual("0x00000001", propArray[0].PropertyName);
            Assert.AreEqual((short)-535, propArray[0].Value);

            // User properties
            Assert.AreEqual("StringProperty", propArray[1].PropertyName);
            Assert.AreEqual("Hello", propArray[1].Value);
            Assert.AreEqual(VTPropertyType.VT_LPSTR, propArray[1].VTType);
            Assert.AreEqual("BooleanProperty", propArray[2].PropertyName);
            Assert.IsTrue((bool?)propArray[2].Value);
            Assert.AreEqual(VTPropertyType.VT_BOOL, propArray[2].VTType);
            Assert.AreEqual("IntegerProperty", propArray[3].PropertyName);
            Assert.AreEqual(3456, propArray[3].Value);
            Assert.AreEqual(VTPropertyType.VT_I4, propArray[3].VTType);
            Assert.AreEqual("DateProperty", propArray[4].PropertyName);
            Assert.AreEqual(testNow, propArray[4].Value);
            Assert.AreEqual(VTPropertyType.VT_FILETIME, propArray[4].VTType);
            Assert.AreEqual("DoubleProperty", propArray[5].PropertyName);
            Assert.AreEqual(1.234567d, propArray[5].Value);
            Assert.AreEqual(VTPropertyType.VT_R8, propArray[5].VTType);
        }
    }

    // As Test_DOCUMENT_SUMMARY_INFO_ADD_CUSTOM, but adding user defined properties with the AddUserDefinedProperty function
    [TestMethod]
    public void TestAddUserDefinedProperty()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("english.presets.doc");

        // Test value for a VT_FILETIME property
        DateTime testNow = DateTime.UtcNow;

        // english.presets.doc has a user defined property section, but no properties other than the codepage
        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(dsiStream);
            OlePropertiesContainer userProperties = co.UserDefinedProperties!;
            userProperties.AddUserDefinedProperty(VTPropertyType.VT_LPSTR, "StringProperty").Value = "Hello";
            userProperties.AddUserDefinedProperty(VTPropertyType.VT_BOOL, "BooleanProperty").Value = true;
            userProperties.AddUserDefinedProperty(VTPropertyType.VT_I4, "IntegerProperty").Value = 3456;
            userProperties.AddUserDefinedProperty(VTPropertyType.VT_FILETIME, "DateProperty").Value = testNow;
            userProperties.AddUserDefinedProperty(VTPropertyType.VT_R8, "DoubleProperty").Value = 1.234567d;

            co.Save(dsiStream);
        }

        ValidateAddedUserDefinedProperties(modifiedStream, testNow);
    }

    // Validate that the user defined properties added by Test_DOCUMENT_SUMMARY_INFO_ADD_CUSTOM / Test_Add_User_Defined_Property are as expected
    private static void ValidateAddedUserDefinedProperties(MemoryStream stream, DateTime testFileTimeValue)
    {
        using var cf = RootStorage.Open(stream);
        using CfbStream cfbStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(cfbStream);
        IList<OleProperty> propArray = co.UserDefinedProperties!.Properties;
        Assert.HasCount(6, propArray);

        // CodePage prop
        Assert.AreEqual(1u, propArray[0].PropertyIdentifier);
        Assert.AreEqual("0x00000001", propArray[0].PropertyName);
        Assert.AreEqual((short)-535, propArray[0].Value);

        // User properties
        Assert.AreEqual("StringProperty", propArray[1].PropertyName);
        Assert.AreEqual("Hello", propArray[1].Value);
        Assert.AreEqual(VTPropertyType.VT_LPSTR, propArray[1].VTType);
        Assert.AreEqual("BooleanProperty", propArray[2].PropertyName);
        Assert.IsTrue((bool?)propArray[2].Value);
        Assert.AreEqual(VTPropertyType.VT_BOOL, propArray[2].VTType);
        Assert.AreEqual("IntegerProperty", propArray[3].PropertyName);
        Assert.AreEqual(3456, propArray[3].Value);
        Assert.AreEqual(VTPropertyType.VT_I4, propArray[3].VTType);
        Assert.AreEqual("DateProperty", propArray[4].PropertyName);
        Assert.AreEqual(testFileTimeValue, propArray[4].Value);
        Assert.AreEqual(VTPropertyType.VT_FILETIME, propArray[4].VTType);
        Assert.AreEqual("DoubleProperty", propArray[5].PropertyName);
        Assert.AreEqual(1.234567d, propArray[5].Value);
        Assert.AreEqual(VTPropertyType.VT_R8, propArray[5].VTType);
    }

    // The names of user defined properties must be unique - adding a duplicate should throw.
    [TestMethod]
    public void TestAddUserDefinedPropertyShouldPreventDuplicates()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("english.presets.doc");
        using var cf = RootStorage.Open(modifiedStream);
        CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(dsiStream);
        OlePropertiesContainer userProperties = co.UserDefinedProperties!;

        userProperties.AddUserDefinedProperty(VTPropertyType.VT_LPSTR, "StringProperty");

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => userProperties.AddUserDefinedProperty(VTPropertyType.VT_LPSTR, "stringproperty"));

        Assert.AreEqual("name", exception.ParamName);
    }

    // Try to read a document which contains Vector/String properties
    // refs https://github.com/ironfede/openmcdf/issues/98
    [TestMethod]
    public void ReadLpwstringVector()
    {
        using var cf = RootStorage.OpenRead("SampleWorkBook_bug98.xls");
        using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
        OlePropertiesContainer co = new(stream);

        OleProperty? docPartsProperty = co.Properties.FirstOrDefault(property => property.PropertyIdentifier == 13); // 13 == PIDDSI_DOCPARTS
        Assert.IsNotNull(docPartsProperty);

        var docPartsValues = docPartsProperty.Value as IList<string>;
        Assert.IsNotNull(docPartsValues);

        Assert.HasCount(3, docPartsValues);
        Assert.AreEqual("Sheet1", docPartsValues[0]);
        Assert.AreEqual("Sheet2", docPartsValues[1]);
        Assert.AreEqual("Sheet3", docPartsValues[2]);
    }

    [TestMethod]
    public void ReadClsidProperty()
    {
        Guid guid = new("15891a95-bf6e-4409-b7d0-3a31c391fa31");
        using var cf = RootStorage.OpenRead("CLSIDPropertyTest.cfs");
        using CfbStream stream = cf.OpenStream("\u0005C3teagxwOttdbfkuIaamtae3Ie");
        OlePropertiesContainer co = new(stream);
        OleProperty clsidProp = co.Properties.First(x => x.PropertyName == "DocumentID");
        Assert.AreEqual(guid, clsidProp.Value);
    }

    // The test file 'report.xls' contains a DocumentSummaryInfo section, but no user defined properties.
    //    This tests adding a new user defined properties section to the existing DocumentSummaryInfo.
    [TestMethod]
    public void AddUserDefinedPropertiesSection()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("report.xls");

        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream dsiStream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(dsiStream);

            Assert.IsNull(co.UserDefinedProperties);

            OlePropertiesContainer newUserDefinedProperties = co.CreateUserDefinedProperties(65001); // 65001 - UTF-8

            Assert.IsNotNull(newUserDefinedProperties.PropertyNames);
            newUserDefinedProperties.PropertyNames[2] = "MyCustomProperty";

            OleProperty createProperty = co.CreateProperty(VTPropertyType.VT_LPSTR, 2);
            createProperty.Value = "Testing";
            newUserDefinedProperties.Add(createProperty);

            co.Save(dsiStream);
        }

        using (var cf = RootStorage.Open(modifiedStream))
        {
            using CfbStream stream = cf.OpenStream(PropertySetNames.DocSummaryInformation);
            OlePropertiesContainer co = new(stream);

            // User defined properties should be present now
            Assert.IsNotNull(co.UserDefinedProperties);
            Assert.AreEqual(65001, co.UserDefinedProperties.Context.CodePage);

            // And the expected properties should the there
            OleProperty[] propArray = co.UserDefinedProperties.Properties.ToArray();
            Assert.HasCount(2, propArray);

            // CodePage prop
            Assert.AreEqual(1u, propArray[0].PropertyIdentifier);
            Assert.AreEqual("0x00000001", propArray[0].PropertyName);
            Assert.AreEqual((short)-535, propArray[0].Value);

            // User properties
            Assert.AreEqual("MyCustomProperty", propArray[1].PropertyName);
            Assert.AreEqual("Testing", propArray[1].Value);
            Assert.AreEqual(VTPropertyType.VT_LPSTR, propArray[1].VTType);
        }
    }

    // A test for the issue described in https://github.com/ironfede/openmcdf/issues/134 where modifying an AppSpecific stream
    // removes any already-existing Dictionary property
    [TestMethod]
    public void TestRetainDictionaryPropertyInAppSpecificStreams()
    {
        using MemoryStream modifiedStream = TestData.CreateMemoryStreamFromFile("Issue134.cfs");

        Dictionary<uint, string> expectedPropertyNames = new()
        {
            [2] = "Document Number",
            [3] = "Revision",
            [4] = "Project Name",
        };

        Guid expectedFmtid0 = Guid.Parse("f0d6d0b1-a0d8-11ce-8aa2-08003601e988");

        using (var cf = RootStorage.Open(modifiedStream, StorageModeFlags.LeaveOpen))
        {
            using CfbStream testStream = cf.OpenStream("Issue134");
            OlePropertiesContainer co = new(testStream);

            Assert.AreEqual(ContainerType.AppSpecific, co.ContainerType);
            Assert.AreEqual(expectedFmtid0, co.FMTID0);
            Assert.AreEqual(1040u, co.Context.Locale);
            CollectionAssert.AreEqual(expectedPropertyNames, co.PropertyNames);

            // Write test file
            co.Save(testStream);
        }

        // Open test file, and check that the property names are still as expected.
        using (var cf = RootStorage.Open(modifiedStream))
        {
            using CfbStream testStream = cf.OpenStream("Issue134");
            OlePropertiesContainer co = new(testStream);

            Assert.AreEqual(ContainerType.AppSpecific, co.ContainerType);
            Assert.AreEqual(expectedFmtid0, co.FMTID0);
            CollectionAssert.AreEqual(expectedPropertyNames, co.PropertyNames);
        }
    }

    // The CodePage property is mandatory in the SummaryInformation set - see https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-oshared/87667163-ea1e-4d67-9eec-47cad74e8030
    //   so we should get an error if it's not present
    [TestMethod]
    public void ShouldThrowWhenNoCodePageInSummaryInformation()
    {
        using var cf = RootStorage.Open("no_codepage.doc", FileMode.Open, StorageModeFlags.Transacted);
        using CfbStream stream = cf.OpenStream(PropertySetNames.SummaryInformation);

        FileFormatException ex = Assert.Throws<FileFormatException>(() => new OlePropertiesContainer(stream));
        Assert.AreEqual("Required CodePage property not present.", ex.Message);
    }
}
