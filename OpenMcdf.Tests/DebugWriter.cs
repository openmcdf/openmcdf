using System.Diagnostics;
using System.Text;

namespace OpenMcdf.Tests;

internal sealed class DebugWriter : TextWriter
{
    static readonly Lazy<DebugWriter> LazyDebugWriter = new();

    public static DebugWriter Default => LazyDebugWriter.Value;

    public override Encoding Encoding => Encoding.Unicode;

    public override void Write(char value) => Debug.Write(value);

    public override void Write(string? value) => Debug.Write(value);

    public override void WriteLine(string? value) => Debug.WriteLine(value);
}
