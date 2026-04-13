namespace OpenMcdf;

/// <summary>
/// Provides utility methods and constants for working with Windows file time values.
/// </summary>
public static class FileTime
{
    /// <summary>
    /// Represents the Windows file time zero value as a UTC <see cref="DateTime"/>.
    /// </summary>
    public static readonly DateTime UtcZero = DateTime.FromFileTimeUtc(0);

    /// <summary>
    /// Determines whether a <see cref="DateTime"/> value represents Windows file time <c>0</c>.
    /// </summary>
    /// <param name="dateTime">The date and time value to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="dateTime"/> equals <see cref="UtcZero"/> after conversion to UTC;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsFileTimeUtcZero(this DateTime dateTime) => dateTime.ToUniversalTime() == UtcZero;
}
