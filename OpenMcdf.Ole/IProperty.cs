namespace OpenMcdf.Ole;

internal interface IProperty : IBinarySerializable
{
    object? Value { get; set; }
}
