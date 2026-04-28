using Avalonia.Platform.Storage;

namespace OpenMcdf.Explorer.Services;

internal sealed class FilePickerService : IFilePickerService
{
    private readonly IStorageProvider storageProvider;

    public FilePickerService(IStorageProvider storageProvider)
    {
        this.storageProvider = storageProvider;
    }

    public async Task<IStorageFile?> PickFileAsync()
    {
        FilePickerOpenOptions options = new()
        {
            AllowMultiple = false,
            Title = "Open",
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Advanced Authoring Format files (*.aaf)")
                {
                    Patterns = new[] { "*.aaf" },
                },
                new("MSI Setup files (*.msi)")
                {
                    Patterns = new[] { "*.msi" },
                },
                new("Hancom Office files (*.hwp)")
                {
                    Patterns = new[] { "*.hwp" },
                },
                new("Office files (*.doc;*.ppt;*.pub;*.xls)")
                {
                    Patterns = new[] { "*.xls", "*.doc", "*.ppt", "*.pub" },
                },
                new("OpenMcdf Test files (*.cfb;*.cfs)")
                {
                    Patterns = new[] { "*.cfb", "*.cfs" },
                },
                new("Outlook messages (*.msg)")
                {
                    Patterns = new[] { "*.msg" },
                },
                new("Thumbnail cache files (Thumbs.db)")
                {
                    Patterns = new[] { "Thumbs.db" },
                },
                new("Solution User Options files (*.suo)")
                {
                    Patterns = new[] { "*.suo" },
                },
                new("All Structured Storage files")
                {
                    Patterns = new[] { "Thumbs.db", "*.aaf", "*.cfs", "*.doc", "*.hwp", "*.msg", "*.msi", "*.ppt", "*.pub", "*.suo", "*.xls" },
                },
                new("All files (*.*)")
                {
                    Patterns = new[] { "*.*" },
                },
            },
        };

        options.SuggestedFileType = options.FileTypeFilter[^2];

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(options);
        return files.SingleOrDefault();
    }

    public async Task<IStorageFile?> PickSaveFileAsync(string suggestedFileName)
    {
        FilePickerSaveOptions options = new()
        {
            Title = "Save As",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ".cfs",
        };

        return await storageProvider.SaveFilePickerAsync(options);
    }

    public async Task<IStorageFile?> PickExportFileAsync(string suggestedFileName)
    {
        FilePickerSaveOptions options = new()
        {
            Title = "Export",
            SuggestedFileName = suggestedFileName,
        };

        return await storageProvider.SaveFilePickerAsync(options);
    }
}
