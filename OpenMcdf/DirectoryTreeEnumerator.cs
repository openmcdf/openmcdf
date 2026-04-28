using System.Collections;

namespace OpenMcdf;

/// <summary>
/// Enumerates the children of a <see cref="DirectoryEntry"/>.
/// </summary>
internal sealed class DirectoryTreeEnumerator : IEnumerator<DirectoryEntry>
{
    private readonly DirectoryEntries directories;
    private readonly DirectoryEntry root;
    private readonly DirectoryTreeTraversalOrderValidator validator = new();
    private readonly Stack<DirectoryEntry> stack = new();
    DirectoryEntry? current;

    internal DirectoryTreeEnumerator(DirectoryEntries directories, DirectoryEntry root)
    {
        this.directories = directories;
        this.root = root;
        Reset();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    public DirectoryEntry Current => current switch
    {
        null => throw new InvalidOperationException("Enumeration has not started. Call MoveNext."),
        _ => current,
    };

    /// <inheritdoc/>
    object IEnumerator.Current => Current;

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (stack.Count == 0)
        {
            current = null;
            return false;
        }

        current = stack.Pop();

        DirectoryEntry? rightSibling = directories.TryGetSibling(current, SiblingType.Right, validator);
        if (rightSibling is not null)
            PushLeft(rightSibling);

        return true;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        current = null;
        stack.Clear();
        validator.Reset();
        if (root.ChildId != StreamId.NoStream)
        {
            DirectoryEntry child = directories.GetDictionaryEntry(root.ChildId);
            PushLeft(child);
        }
    }

    private void PushLeft(DirectoryEntry? node)
    {
        while (node is not null)
        {
            // Entries pushed onto the stack must always be less than the previous entry
            if (stack.Count > 0 && stack.Peek() is { } peek)
            {
                int compare = DirectoryEntryComparer.Compare(node.NameCharSpan, peek.NameCharSpan);
                ThrowHelper.ThrowIfInvalidBinarySearchTree(compare >= 0);
            }

            stack.Push(node);
            node = directories.TryGetSibling(node, SiblingType.Left, validator);
        }
    }
}
