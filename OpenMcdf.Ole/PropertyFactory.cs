using System.Text;

namespace OpenMcdf.Ole;

internal abstract class PropertyFactory
{
    static PropertyFactory()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ITypedPropertyValue CreateProperty(VTPropertyType vType, int codePage, uint propertyIdentifier, bool isVariant = false)
    {
        ITypedPropertyValue pr = (VTPropertyType)((ushort)vType & 0x00FF) switch
        {
            VTPropertyType.VT_I1 => new VT_I1_Property(vType, isVariant),
            VTPropertyType.VT_I2 => new VT_I2_Property(vType, isVariant),
            VTPropertyType.VT_I4 => new VT_I4_Property(vType, isVariant),
            VTPropertyType.VT_I8 => new VT_I8_Property(vType, isVariant),
            VTPropertyType.VT_R4 => new VT_R4_Property(vType, isVariant),
            VTPropertyType.VT_R8 => new VT_R8_Property(vType, isVariant),
            VTPropertyType.VT_CY => new VT_CY_Property(vType, isVariant),
            VTPropertyType.VT_DATE => new VT_DATE_Property(vType, isVariant),
            VTPropertyType.VT_INT => new VT_INT_Property(vType, isVariant),
            VTPropertyType.VT_UINT => new VT_UINT_Property(vType, isVariant),
            VTPropertyType.VT_UI1 => new VT_UI1_Property(vType, isVariant),
            VTPropertyType.VT_UI2 => new VT_UI2_Property(vType, isVariant),
            VTPropertyType.VT_UI4 => new VT_UI4_Property(vType, isVariant),
            VTPropertyType.VT_UI8 => new VT_UI8_Property(vType, isVariant),
            VTPropertyType.VT_BSTR => new VT_LPSTR_Property(vType, codePage, isVariant),
            VTPropertyType.VT_LPSTR => CreateLpstrProperty(vType, codePage, propertyIdentifier, isVariant),
            VTPropertyType.VT_LPWSTR => new VT_LPWSTR_Property(vType, isVariant),
            VTPropertyType.VT_FILETIME => new VT_FILETIME_Property(vType, isVariant),
            VTPropertyType.VT_DECIMAL => new VT_DECIMAL_Property(vType, isVariant),
            VTPropertyType.VT_BOOL => new VT_BOOL_Property(vType, isVariant),
            VTPropertyType.VT_EMPTY => new VT_EMPTY_Property(vType, isVariant),
            VTPropertyType.VT_NULL => new VT_NULL_Property(vType, isVariant),
            VTPropertyType.VT_VARIANT_VECTOR => new VT_VariantVector(vType, codePage, isVariant, this, propertyIdentifier),
            VTPropertyType.VT_CF => new VT_CF_Property(vType, isVariant),
            VTPropertyType.VT_BLOB_OBJECT or VTPropertyType.VT_BLOB => new VT_BLOB_Property(vType, isVariant),
            VTPropertyType.VT_CLSID => new VT_CLSID_Property(vType, isVariant),
            _ => throw new ArgumentException("Unrecognized property type"),
        };
        return pr;
    }

    protected virtual ITypedPropertyValue CreateLpstrProperty(VTPropertyType vType, int codePage, uint propertyIdentifier, bool isVariant) => new VT_LPSTR_Property(vType, codePage, isVariant);

    #region Property implementations

    private sealed class VT_EMPTY_Property : TypedPropertyValue<object>
    {
        public VT_EMPTY_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override object? ReadScalarValue(BinaryReader br) => null;

        public override void WriteScalarValue(BinaryWriter bw, object pValue)
        {
        }
    }

    private sealed class VT_NULL_Property(VTPropertyType vType, bool isVariant) : TypedPropertyValue<object>(vType, isVariant)
    {
        public override object? ReadScalarValue(BinaryReader br) => null;

        public override void WriteScalarValue(BinaryWriter bw, object pValue)
        {
        }
    }

    private sealed class VT_I1_Property : TypedPropertyValue<sbyte>
    {
        public VT_I1_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override sbyte ReadScalarValue(BinaryReader br)
        {
            sbyte r = br.ReadSByte();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, sbyte pValue) => bw.Write(pValue);
    }

    private sealed class VT_UI1_Property : TypedPropertyValue<byte>
    {
        public VT_UI1_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override byte ReadScalarValue(BinaryReader br)
        {
            byte r = br.ReadByte();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, byte pValue) => bw.Write(pValue);
    }

    private sealed class VT_UI4_Property : TypedPropertyValue<uint>
    {
        public VT_UI4_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override uint ReadScalarValue(BinaryReader br)
        {
            uint r = br.ReadUInt32();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, uint pValue) => bw.Write(pValue);
    }

    private sealed class VT_UI8_Property : TypedPropertyValue<ulong>
    {
        public VT_UI8_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override ulong ReadScalarValue(BinaryReader br)
        {
            ulong r = br.ReadUInt64();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, ulong pValue) => bw.Write(pValue);
    }

    private sealed class VT_I2_Property : TypedPropertyValue<short>
    {
        public VT_I2_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override short ReadScalarValue(BinaryReader br)
        {
            short r = br.ReadInt16();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, short pValue) => bw.Write(pValue);
    }

    private sealed class VT_UI2_Property : TypedPropertyValue<ushort>
    {
        public VT_UI2_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override ushort ReadScalarValue(BinaryReader br)
        {
            ushort r = br.ReadUInt16();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, ushort pValue) => bw.Write(pValue);
    }

    private sealed class VT_I4_Property : TypedPropertyValue<int>
    {
        public VT_I4_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override int ReadScalarValue(BinaryReader br)
        {
            int r = br.ReadInt32();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, int pValue) => bw.Write(pValue);
    }

    private sealed class VT_I8_Property : TypedPropertyValue<long>
    {
        public VT_I8_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override long ReadScalarValue(BinaryReader br)
        {
            long r = br.ReadInt64();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, long pValue) => bw.Write(pValue);
    }

    private sealed class VT_INT_Property : TypedPropertyValue<int>
    {
        public VT_INT_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override int ReadScalarValue(BinaryReader br)
        {
            int r = br.ReadInt32();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, int pValue) => bw.Write(pValue);
    }

    private sealed class VT_UINT_Property : TypedPropertyValue<uint>
    {
        public VT_UINT_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override uint ReadScalarValue(BinaryReader br)
        {
            uint r = br.ReadUInt32();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, uint pValue) => bw.Write(pValue);
    }

    private sealed class VT_R4_Property : TypedPropertyValue<float>
    {
        public VT_R4_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override float ReadScalarValue(BinaryReader br)
        {
            float r = br.ReadSingle();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, float pValue) => bw.Write(pValue);
    }

    private sealed class VT_R8_Property : TypedPropertyValue<double>
    {
        public VT_R8_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override double ReadScalarValue(BinaryReader br)
        {
            double r = br.ReadDouble();
            return r;
        }

        public override void WriteScalarValue(BinaryWriter bw, double pValue) => bw.Write(pValue);
    }

    private sealed class VT_CY_Property : TypedPropertyValue<long>
    {
        public VT_CY_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override long ReadScalarValue(BinaryReader br)
        {
            long temp = br.ReadInt64();

            long tmp = temp / 10000;

            return tmp;
        }

        public override void WriteScalarValue(BinaryWriter bw, long pValue) => bw.Write(pValue * 10000);
    }

    private sealed class VT_DATE_Property : TypedPropertyValue<DateTime>
    {
        public VT_DATE_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override DateTime ReadScalarValue(BinaryReader br)
        {
            double temp = br.ReadDouble();

            return DateTime.FromOADate(temp);
        }

        public override void WriteScalarValue(BinaryWriter bw, DateTime pValue) => bw.Write(pValue.ToOADate());
    }

    protected class VT_LPSTR_Property : TypedPropertyValue<string>
    {
        private readonly int codePage;

        public VT_LPSTR_Property(VTPropertyType vType, int codePage, bool isVariant)
            : base(vType, isVariant)
        {
            this.codePage = codePage;
        }

        public override string ReadScalarValue(BinaryReader br) => br.ReadCodePageString(Encoding.GetEncoding(codePage));

        public override void WriteScalarValue(BinaryWriter bw, string pValue) => bw.WriteCodePageString(pValue, Encoding.GetEncoding(codePage));
    }

    protected sealed class VT_Unaligned_LPSTR_Property : VT_LPSTR_Property
    {
        public VT_Unaligned_LPSTR_Property(VTPropertyType vType, int codePage, bool isVariant)
            : base(vType, codePage, isVariant)
        {
            NeedsPadding = false;
        }
    }

    private sealed class VT_LPWSTR_Property : TypedPropertyValue<string>
    {
        public VT_LPWSTR_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override string ReadScalarValue(BinaryReader br) => br.ReadLpwstr();

        public override void WriteScalarValue(BinaryWriter bw, string pValue) => bw.WriteLpwstr(pValue);
    }

    private sealed class VT_FILETIME_Property : TypedPropertyValue<DateTime>
    {
        public VT_FILETIME_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override DateTime ReadScalarValue(BinaryReader br)
        {
            long tmp = br.ReadInt64();

            return DateTime.FromFileTimeUtc(tmp);
        }

        public override void WriteScalarValue(BinaryWriter bw, DateTime pValue) => bw.Write(pValue.ToFileTimeUtc());
    }

    private sealed class VT_DECIMAL_Property : TypedPropertyValue<decimal>
    {
        public VT_DECIMAL_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override decimal ReadScalarValue(BinaryReader br)
        {
            decimal d;

            br.ReadInt16(); // wReserved
            byte scale = br.ReadByte();
            byte sign = br.ReadByte();

            uint u = br.ReadUInt32();
            d = Convert.ToDecimal(Math.Pow(2, 64)) * u;
            d += br.ReadUInt64();

            if (sign != 0)
                d = -d;
            d /= (decimal)Math.Pow(10, scale);

            propertyValue = d;
            return d;
        }

        public override void WriteScalarValue(BinaryWriter bw, decimal pValue)
        {
            int[] parts = decimal.GetBits(pValue);

            bool sign = (parts[3] & 0x80000000) != 0;
            byte scale = (byte)((parts[3] >> 16) & 0x7F);

            bw.Write((short)0);
            bw.Write(scale);
            bw.Write(sign ? (byte)0x80 : (byte)0);

            bw.Write(parts[2]);
            bw.Write(parts[0]);
            bw.Write(parts[1]);
        }
    }

    private sealed class VT_BOOL_Property : TypedPropertyValue<bool>
    {
        public VT_BOOL_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override bool ReadScalarValue(BinaryReader br)
        {
            propertyValue = br.ReadUInt16() == 0xFFFF;
            return (bool)propertyValue;
            // br.ReadUInt16();//padding
        }

        public override void WriteScalarValue(BinaryWriter bw, bool pValue) => bw.Write(pValue ? (ushort)0xFFFF : (ushort)0);
    }

    private sealed class VT_CF_Property : TypedPropertyValue<object>
    {
        public VT_CF_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override object ReadScalarValue(BinaryReader br)
        {
            uint size = br.ReadUInt32();
            byte[] data = br.ReadBytes((int)size);
            return data;
            // br.ReadUInt16();//padding
        }

        public override void WriteScalarValue(BinaryWriter bw, object pValue)
        {
            if (pValue is not byte[] r)
            {
                bw.Write(0u);
            }
            else
            {
                bw.Write((uint)r.Length);
                bw.Write(r);
            }
        }
    }

    private sealed class VT_BLOB_Property : TypedPropertyValue<object>
    {
        public VT_BLOB_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override object ReadScalarValue(BinaryReader br)
        {
            uint size = br.ReadUInt32();
            byte[] data = br.ReadBytes((int)size);
            return data;
        }

        public override void WriteScalarValue(BinaryWriter bw, object pValue)
        {
            if (pValue is not byte[] r)
            {
                bw.Write(0u);
            }
            else
            {
                bw.Write((uint)r.Length);
                bw.Write(r);
            }
        }
    }

    private sealed class VT_CLSID_Property : TypedPropertyValue<Guid>
    {
        public VT_CLSID_Property(VTPropertyType vType, bool isVariant)
            : base(vType, isVariant)
        {
        }

        public override Guid ReadScalarValue(BinaryReader br)
        {
            byte[] data = br.ReadBytes(16);
            return new Guid(data);
        }

        public override void WriteScalarValue(BinaryWriter bw, Guid pValue)
        {
            bw.Write(pValue.ToByteArray());
        }
    }

    private sealed class VT_VariantVector : TypedPropertyValue<object>
    {
        private readonly int codePage;
        private readonly PropertyFactory factory;
        private readonly uint propertyIdentifier;

        public VT_VariantVector(VTPropertyType vType, int codePage, bool isVariant, PropertyFactory factory, uint propertyIdentifier)
            : base(vType, isVariant)
        {
            this.codePage = codePage;
            this.factory = factory;
            this.propertyIdentifier = propertyIdentifier;
            NeedsPadding = false;
        }

        public override object ReadScalarValue(BinaryReader br)
        {
            var vType = (VTPropertyType)br.ReadUInt16();
            br.ReadUInt16(); // Ushort Padding

            ITypedPropertyValue p = factory.CreateProperty(vType, codePage, propertyIdentifier, true);
            p.Read(br);
            return p;
        }

        public override void WriteScalarValue(BinaryWriter bw, object pValue)
        {
            var p = (ITypedPropertyValue)pValue;

            p.Write(bw);
        }
    }

    #endregion

}
