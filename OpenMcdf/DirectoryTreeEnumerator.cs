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

        DirectoryEntry? previous = current;
        current = stack.Pop();

        // An in-order traversal of a valid binary search tree yields entries in strictly increasing order.
        // Checking this detects entries reachable from more than one parent before their subtrees are
        // traversed repeatedly, which could otherwise cause exponential enumeration time.
        if (previous is not null)
        {
            int compare = DirectoryEntryComparer.Compare(current.NameCharSpan, previous.NameCharSpan);
            ThrowHelper.ThrowIfInvalidBinarySearchTree(compare <= 0);
        }

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
