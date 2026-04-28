namespace OpenMcdf;

internal sealed class DirectoryTreeTraversalOrderValidator : IDirectoryTreeValidator
{
    public void Reset()
    {
    }

    public void Validate(DirectoryEntry? entry, DirectoryEntry? sibling, SiblingType siblingType)
    {
        if (entry is null || sibling is null)
            return;

        ThrowHelper.ThrowIfInvalidColor(entry, sibling);

        int compare = DirectoryEntryComparer.Compare(sibling.NameCharSpan, entry.NameCharSpan);
        ThrowHelper.ThrowIfInvalidBinarySearchTree((siblingType is SiblingType.Left && compare >= 0) || (siblingType is SiblingType.Right && compare <= 0));
    }
}
