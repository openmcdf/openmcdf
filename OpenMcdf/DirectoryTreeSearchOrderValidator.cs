namespace OpenMcdf;

internal sealed class DirectoryTreeSearchOrderValidator : IDirectoryTreeValidator
{
    DirectoryEntry? min;
    DirectoryEntry? max;

    public void Reset()
    {
        min = null;
        max = null;
    }

    public void Validate(DirectoryEntry? entry, DirectoryEntry? sibling, SiblingType siblingType)
    {
        if (entry is null || sibling is null)
            return;

        ThrowHelper.ThrowIfInvalidColor(entry, sibling);

        if (siblingType is SiblingType.Left)
            max = entry;
        else
            min = entry;

        if (min is not null)
        {
            int compare2 = DirectoryEntryComparer.Compare(min.NameCharSpan, sibling.NameCharSpan);
            ThrowHelper.ThrowIfInvalidBinarySearchTree(compare2 >= 0);
        }

        if (max is not null)
        {
            int compare2 = DirectoryEntryComparer.Compare(sibling.NameCharSpan, max.NameCharSpan);
            ThrowHelper.ThrowIfInvalidBinarySearchTree(compare2 >= 0);
        }
    }
}
