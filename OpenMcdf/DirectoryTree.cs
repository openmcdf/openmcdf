using System.Diagnostics.CodeAnalysis;

namespace OpenMcdf;

/// <summary>
/// Encapsulates adding and removing <see cref="DirectoryEntry"/> objects to a red-black tree.
/// </summary>
internal sealed class DirectoryTree : ContextBase
{
    internal enum RelationType
    {
        LeftSibling,
        RightSibling,
        Root,
    }

    private readonly DirectoryEntry root;

    public DirectoryTree(RootContextSite rootContextSite, DirectoryEntry root)
        : base(rootContextSite)
    {
        this.root = root;
    }

    private DirectoryEntries Directories => Context.DirectoryEntries;

    public bool TryGetDirectoryEntry(string name, [MaybeNullWhen(false)] out DirectoryEntry entry)
    {
        if (!Directories.TryGetDictionaryEntry(root.ChildId, true, out DirectoryEntry? child))
        {
            entry = null;
            return false;
        }

        ReadOnlySpan<char> nameSpan = name.AsSpan();
        DirectoryTreeSearchOrderValidator validator = new();
        while (child is not null)
        {
            int compare = DirectoryEntryComparer.Compare(nameSpan, child.NameCharSpan);
            if (compare == 0)
            {
                entry = child;
                return true;
            }

            SiblingType siblingType = compare < 0 ? SiblingType.Left : SiblingType.Right;
            child = Directories.TryGetSibling(child, siblingType, validator);
        }

        entry = null;
        return false;
    }

    DirectoryEntry GetParent(DirectoryEntry entry, out RelationType relation)
    {
        if (!TryGetParent(entry, out DirectoryEntry? parent, out relation))
            throw new FileFormatException($"DirectoryEntry {entry} has no parent.");
        return parent;
    }

    bool TryGetParent(DirectoryEntry entry, [MaybeNullWhen(false)] out DirectoryEntry parent, out RelationType relation)
    {
        if (!Directories.TryGetDictionaryEntry(root.ChildId, true, out DirectoryEntry? child))
        {
            parent = null;
            relation = RelationType.Root;
            return false;
        }

        parent = root;
        relation = RelationType.Root;
        DirectoryTreeSearchOrderValidator validator = new();
        while (child is not null)
        {
            int compare = DirectoryEntryComparer.Compare(entry.NameCharSpan, child.NameCharSpan);
            if (compare < 0)
            {
                parent = child;
                relation = RelationType.LeftSibling;
                child = Directories.TryGetSibling(child, SiblingType.Left, validator);
            }
            else if (compare > 0)
            {
                parent = child;
                relation = RelationType.RightSibling;
                child = Directories.TryGetSibling(child, SiblingType.Right, validator);
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    public void Add(DirectoryEntry entry)
    {
        if (!Directories.TryGetDictionaryEntry(root.ChildId, true, out DirectoryEntry? currentEntry))
        {
            root.ChildId = entry.Id;
            Directories.Write(root);
            Directories.Write(entry);
            return;
        }

        DirectoryTreeSearchOrderValidator validator = new();

        while (true)
        {
            int compare = DirectoryEntryComparer.Compare(entry.NameCharSpan, currentEntry.NameCharSpan);
            if (compare < 0)
            {
                if (currentEntry.LeftSiblingId == StreamId.NoStream)
                {
                    currentEntry.LeftSiblingId = entry.Id;
                    Directories.Write(currentEntry);
                    Directories.Write(entry);
                    return;
                }

                currentEntry = Directories.GetSibling(currentEntry, SiblingType.Left, validator);
            }
            else if (compare > 0)
            {
                if (currentEntry.RightSiblingId == StreamId.NoStream)
                {
                    currentEntry.RightSiblingId = entry.Id;
                    Directories.Write(currentEntry);
                    Directories.Write(entry);
                    return;
                }

                currentEntry = Directories.GetSibling(currentEntry, SiblingType.Right, validator);
            }
            else
            {
                throw new IOException($"{entry.Type} \"{entry.NameString}\" already exists.");
            }
        }
    }

    void SetRelation(DirectoryEntry entry, RelationType relation, uint value)
    {
        switch (relation)
        {
            case RelationType.LeftSibling:
                entry.LeftSiblingId = value;
                break;
            case RelationType.RightSibling:
                entry.RightSiblingId = value;
                break;
            case RelationType.Root:
                root.ChildId = value;
                break;
        }
    }

    public void Remove(DirectoryEntry entry)
    {
        DirectoryEntry parent = GetParent(entry, out RelationType relation);

        if (entry.LeftSiblingId == StreamId.NoStream)
        {
            SetRelation(parent, relation, entry.RightSiblingId);
            Directories.Write(parent);
        }
        else
        {
            SetRelation(parent, relation, entry.LeftSiblingId);
            Directories.Write(parent);

            if (entry.RightSiblingId != StreamId.NoStream)
            {
                DirectoryTreeSearchOrderValidator validator = new();
                DirectoryEntry newRightChildParent = Directories.GetSibling(entry, SiblingType.Left, validator);
                while (newRightChildParent.RightSiblingId != StreamId.NoStream)
                    newRightChildParent = Directories.GetSibling(newRightChildParent, SiblingType.Right, validator);
                newRightChildParent.RightSiblingId = entry.RightSiblingId;
                Directories.Write(newRightChildParent);
            }
        }

        entry.Recycle();
        Directories.Write(entry);
    }

    [ExcludeFromCodeCoverage]
    internal void Validate()
    {
        if (Directories.TryGetDictionaryEntry(root.ChildId, true, out DirectoryEntry? child))
        {
            DirectoryTreeTraversalOrderValidator validator = new();
            Validate(child, validator);
        }
    }

    [ExcludeFromCodeCoverage]
    void Validate(DirectoryEntry entry, DirectoryTreeTraversalOrderValidator validator)
    {
        DirectoryEntry? leftSibling = Directories.TryGetSibling(entry, SiblingType.Left, validator);
        if (leftSibling is not null)
            Validate(leftSibling, validator);

        DirectoryEntry? rightSibling = Directories.TryGetSibling(entry, SiblingType.Right, validator);
        if (rightSibling is not null)
            Validate(rightSibling, validator);

        if (entry.ChildId != StreamId.NoStream)
        {
            DirectoryTreeTraversalOrderValidator childValidator = new();
            DirectoryEntry child = Directories.GetDictionaryEntry(entry.ChildId);
            Validate(child, childValidator);
        }
    }

    [ExcludeFromCodeCoverage]
    internal void WriteTrace(TextWriter writer)
    {
        if (Directories.TryGetDictionaryEntry(root.ChildId, true, out DirectoryEntry? child))
            WriteTrace(writer, child, 0);
    }

    [ExcludeFromCodeCoverage]
    void WriteTrace(TextWriter writer, DirectoryEntry entry, int indent)
    {
        DirectoryTreeTraversalOrderValidator validator = new();
        DirectoryEntry? rightSibling = Directories.TryGetSibling(entry, SiblingType.Right, validator);
        if (rightSibling is not null)
            WriteTrace(writer, rightSibling, indent + 1);

        for (int i = 0; i < indent; i++)
            writer.Write("  ");
        writer.WriteLine(entry);

        DirectoryEntry? leftSibling = Directories.TryGetSibling(entry, SiblingType.Left, validator);
        if (leftSibling is not null)
            WriteTrace(writer, leftSibling, indent + 1);
    }
}
