namespace OpenMcdf;

internal interface IDirectoryTreeValidator
{
    void Reset();

    void Validate(DirectoryEntry? entry, DirectoryEntry? sibling, SiblingType siblingType);
}
