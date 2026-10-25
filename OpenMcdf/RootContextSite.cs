namespace OpenMcdf;

/// <summary>
/// A site for the <see cref="RootContext"/> object, to allow switching streams.
/// </summary>
internal sealed class RootContextSite
{
    // Deleted directory entries are recycled, so count deletions to invalidate the storages and streams opened before them
    readonly Dictionary<uint, int> entryDeletionCounts = new();
    RootContext? context;

    internal RootContext Context => context!;

    /// <summary>
    /// Gets the number of times the directory entries and sector chains have been rewritten, which invalidates open storages and streams.
    /// </summary>
    internal int Generation { get; private set; }

    internal void Invalidate() => Generation++;

    internal int GetEntryDeletionCount(uint streamId) => entryDeletionCounts.TryGetValue(streamId, out int count) ? count : 0;

    internal void InvalidateEntry(uint streamId) => entryDeletionCounts[streamId] = GetEntryDeletionCount(streamId) + 1;

    internal void Switch(RootContext context)
    {
        this.context = context;
    }
}
