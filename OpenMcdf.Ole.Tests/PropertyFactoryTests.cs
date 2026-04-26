using System.Text;

namespace OpenMcdf.Ole.Tests;

[TestClass]
public class PropertyFactoryTests
{
    private const int CodePage = 1252;
    private const uint PropertyIdentifier = 42;

    public static IEnumerable<object[]> SupportedScalarRoundTripCases()
    {
        yield return new object[] { VTPropertyType.VT_EMPTY, null! };
        yield return new object[] { VTPropertyType.VT_NULL, null! };
        yield return new object[] { VTPropertyType.VT_I1, (sbyte)-12 };
        yield return new object[] { VTPropertyType.VT_I2, (short)-1234 };
        yield return new object[] { VTPropertyType.VT_I4, -1234567 };
        yield return new object[] { VTPropertyType.VT_I8, -1234567890123L };
        yield return new object[] { VTPropertyType.VT_R4, 1.25f };
        yield return new object[] { VTPropertyType.VT_R8, 123.456789d };
        yield return new object[] { VTPropertyType.VT_CY, 1234567L };
        yield return new object[] { VTPropertyType.VT_DATE, new DateTime(2024, 6, 1, 12, 34, 56, DateTimeKind.Utc) };
        yield return new object[] { VTPropertyType.VT_BSTR, "BSTR value" };
        yield return new object[] { VTPropertyType.VT_BOOL, true };
        yield return new object[] { VTPropertyType.VT_DECIMAL, 12345.6789m };
        yield return new object[] { VTPropertyType.VT_UI1, (byte)250 };
        yield return new object[] { VTPropertyType.VT_UI2, (ushort)65000 };
        yield return new object[] { VTPropertyType.VT_UI4, 4294967290U };
        yield return new object[] { VTPropertyType.VT_UI8, 18446744073709551610UL };
        yield return new object[] { VTPropertyType.VT_INT, -987654321 };
        yield return new object[] { VTPropertyType.VT_UINT, 3987654321U };
        yield return new object[] { VTPropertyType.VT_LPSTR, "Hello World!" };
        yield return new object[] { VTPropertyType.VT_LPWSTR, "こんにちは世界" };
        yield return new object[] { VTPropertyType.VT_FILETIME, new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc) };
        yield return new object[] { VTPropertyType.VT_BLOB, new byte[] { 1, 2, 3, 4, 5 } };
        yield return new object[] { VTPropertyType.VT_BLOB_OBJECT, new byte[] { 10, 20, 30 } };
        yield return new object[] { VTPropertyType.VT_CF, new byte[] { 9, 8, 7 } };
        yield return new object[] { VTPropertyType.VT_CLSID, Guid.Parse("15891a95-bf6e-4409-b7d0-3a31c391fa31") };
    }

    [TestMethod]
    [DynamicData(nameof(SupportedScalarRoundTripCases))]
    public void RoundTripSerialization(VTPropertyType vType, object value)
    {
        PropertyFactory factory = DefaultPropertyFactory.Default;

        ITypedPropertyValue property = factory.CreateProperty(vType, CodePage, Encoding.GetEncoding(CodePage), PropertyIdentifier);

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            property.Value = value;
            property.Write(bw);
        }

        ITypedPropertyValue property2 = factory.CreateProperty(vType, CodePage, Encoding.GetEncoding(CodePage), PropertyIdentifier);
        ms.Position = 0;
        using var br = new BinaryReader(ms, Encoding.UTF8, true);
        var actualType = (VTPropertyType)br.ReadUInt16();
        br.ReadUInt16(); // Ushort Padding
        property2.Read(br);

        Assert.AreEqual(vType, actualType);
        if (value is byte[] expectedBytes)
        {
            CollectionAssert.AreEqual(expectedBytes, (byte[])property2.Value!);
        }
        else
        {
            Assert.AreEqual(value, property2.Value);
        }
    }
}
