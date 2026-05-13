using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;

namespace OpenMcdf.Ole.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
[HideColumns(Column.AllocRatio)]
[MarkdownExporter]
public class OpenMcdfOleReadBenchmarks : IDisposable
{
    private RootStorage? rootStorageLpstr;
    private RootStorage? rootStorageLWpstr;
    private CfbStream? summaryInformationStream;
    private CfbStream? documentSummaryInformationStream;
    private CfbStream? winUnicodeDocumentSummaryInformationStream;

    public void Dispose()
    {
        summaryInformationStream?.Dispose();
        documentSummaryInformationStream?.Dispose();
        winUnicodeDocumentSummaryInformationStream?.Dispose();
        rootStorageLpstr?.Dispose();
        rootStorageLWpstr?.Dispose();
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        rootStorageLpstr = RootStorage.OpenRead("2custom.doc");
        rootStorageLWpstr = RootStorage.OpenRead("winUnicodeDictionary.doc");
        summaryInformationStream = rootStorageLpstr.OpenStream(PropertySetNames.SummaryInformation);
        documentSummaryInformationStream = rootStorageLpstr.OpenStream(PropertySetNames.DocSummaryInformation);
        winUnicodeDocumentSummaryInformationStream = rootStorageLWpstr.OpenStream(PropertySetNames.DocSummaryInformation);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => Dispose();

    [Benchmark]
    public OlePropertiesContainer ReadSummaryInformation() => new(summaryInformationStream!);

    [Benchmark]
    public OlePropertiesContainer ReadDocumentSummaryInformation() => new(documentSummaryInformationStream!);

    [Benchmark]
    public OlePropertiesContainer ReadWinUnicodeDocumentSummaryInformation() => new(winUnicodeDocumentSummaryInformationStream!);
}
