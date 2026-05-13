using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;

namespace OpenMcdf.Ole.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
[HideColumns(Column.AllocRatio)]
[MarkdownExporter]
public class OpenMcdfOleWriteBenchmarks : IDisposable
{
    private RootStorage? rootStorageLpstr;
    private CfbStream? summaryInformationStream;
    private CfbStream? documentSummaryInformationStream;
    private OlePropertiesContainer? summaryInformationContainer;
    private OlePropertiesContainer? documentSummaryInformationContainer;

    public void Dispose()
    {
        summaryInformationStream?.Dispose();
        documentSummaryInformationStream?.Dispose();
        rootStorageLpstr?.Dispose();
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Copy the test file into memory so we don't keep the file locked.
        // Note: documentStream ownership transferred to RootStorage for disposal.
        MemoryStream documentStream = new();
        using (FileStream fileStream = File.OpenRead("2custom.doc"))
            fileStream.CopyTo(documentStream);

        _ = documentStream.Seek(0, SeekOrigin.Begin);
        rootStorageLpstr = RootStorage.Open(documentStream);
        summaryInformationStream = rootStorageLpstr.OpenStream(PropertySetNames.SummaryInformation);
        documentSummaryInformationStream = rootStorageLpstr.OpenStream(PropertySetNames.DocSummaryInformation);
        summaryInformationContainer = new(summaryInformationStream);
        documentSummaryInformationContainer = new(documentSummaryInformationStream);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => Dispose();

    [Benchmark]
    public void WriteSummaryInformation() => summaryInformationContainer!.Save(summaryInformationStream!);

    [Benchmark]
    public void WriteDocumentSummaryInformation() => documentSummaryInformationContainer!.Save(documentSummaryInformationStream!);
}
