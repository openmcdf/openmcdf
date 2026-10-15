namespace OpenMcdf;

/// <summary>
/// A site for the <see cref="RootContext"/> object, to allow switching streams.
/// </summary>
internal sealed class RootContextSite
{
    RootContext? context;

    internal RootContext Context => context!;

    /// <summary>
    /// Gets the number of times the directory entries and sector chains have been rewritten, which invalidates open storages and streams.
    /// </summary>
    internal int Generation { get; private set; }

    internal void Invalidate() => Generation++;

    internal void Switch(RootContext context)
    {
        this.context = context;
    }
}
