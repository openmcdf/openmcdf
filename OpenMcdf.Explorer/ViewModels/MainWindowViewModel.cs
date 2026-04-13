using Avalonia.Platform.Storage;
using AvaloniaHex.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMcdf.Explorer.Models;
using OpenMcdf.Explorer.Services;
using OpenMcdf.Ole;
using System.Collections;
using System.Collections.ObjectModel;

namespace OpenMcdf.Explorer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService dialogService;
    private readonly IFilePickerService filePickerService;

    private RootStorage? rootStorage;
    private CfbStream? currentStream;
    private EntryInfoNode? selectedNode;

    [ObservableProperty]
    public partial string FilePath { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDocumentLoaded { get; private set; }

    [ObservableProperty]
    public partial IBinaryDocument? HexDocument { get; private set; }

    public ObservableCollection<EntryInfoNode> Nodes { get; private set; } = [];

    public ObservableCollection<EntryProperty> EntryProperties { get; } = [];

    public ObservableCollection<OlePropertyItem> OleProperties { get; } = [];

    public ObservableCollection<OlePropertyItem> UserDefinedProperties { get; } = [];

    public bool IsNodeSelected => selectedNode is not null && selectedNode.Parent is not null;

    public bool IsStorageSelected => selectedNode?.IsStorage == true;

    public bool IsStreamSelected => selectedNode?.IsStorage == false && selectedNode?.Parent is not null;

    // Design-time constructor
    public MainWindowViewModel()
    {
        dialogService = null!;
        filePickerService = null!;
    }

    public MainWindowViewModel(IDialogService dialogService, IFilePickerService filePickerService)
    {
        this.dialogService = dialogService;
        this.filePickerService = filePickerService;
    }

    public void Dispose()
    {
        CloseCurrentFile();
    }

    private void CloseCurrentFile()
    {
        currentStream?.Dispose();
        currentStream = null;
        HexDocument = null;
        rootStorage?.Dispose();
        rootStorage = null;
    }

    [RelayCommand]
    private async Task TryCloseCurrentFile()
    {
        try
        {
            await SetSelectedNodeAsync(null);
            FilePath = string.Empty;
            IsDocumentLoaded = false;
            Nodes.Clear();
            CloseCurrentFile();
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error closing file: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TryCreateNewFile()
    {
        await TryCloseCurrentFile();

        try
        {
            string filePath = Path.GetTempFileName();
            rootStorage = RootStorage.Create(filePath, Version.V3);
            FilePath = filePath;
            IsDocumentLoaded = true;

            await RefreshNodesAsync();
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error creating new file: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task TryOpenFile()
    {
        IStorageFile? file = await filePickerService.PickFileAsync();
        if (file is not null)
        {
            await TryOpenFile(file.Path.LocalPath);
        }
    }

    public async Task TryOpenFile(string filePath)
    {
        await TryCloseCurrentFile();

        try
        {
            rootStorage = RootStorage.Open(filePath, FileMode.Open, StorageModeFlags.Transacted);
            FilePath = filePath;
            IsDocumentLoaded = true;

            await RefreshNodesAsync();
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error opening file: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsDocumentLoaded))]
    private async Task TrySave()
    {
        if (rootStorage is null)
            return;

        try
        {
            await ApplyHexChangesAsync();

            rootStorage.Commit();
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error saving file: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsDocumentLoaded))]
    private async Task TrySaveAs()
    {
        if (rootStorage is null)
            return;

        try
        {
            string suggestedName = Path.GetFileName(FilePath);
            IStorageFile? file = await filePickerService.PickSaveFileAsync(suggestedName);
            if (file is null)
                return;

            string newPath = file.Path.LocalPath;
            await ApplyHexChangesAsync();
            rootStorage.SwitchTo(newPath);
            FilePath = newPath;
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error saving file: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsStorageSelected))]
    private async Task AddStorage()
    {
        if (rootStorage is null || selectedNode is null)
            return;

        try
        {
            string? name = await dialogService.ShowInputDialogAsync("Add Storage", "Enter storage name:");
            if (string.IsNullOrEmpty(name))
                return;

            Storage childStorage = selectedNode.Storage!.CreateStorage(name);
            selectedNode.AddChildStorage(childStorage);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error adding storage: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsStorageSelected))]
    private async Task AddStream()
    {
        if (rootStorage is null || selectedNode is null)
            return;

        try
        {
            string? name = await dialogService.ShowInputDialogAsync("Add Stream", "Enter stream name:");
            if (string.IsNullOrEmpty(name))
                return;

            using CfbStream stream = selectedNode.Storage!.CreateStream(name);
            selectedNode.AddChildStream(stream.EntryInfo);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error adding stream: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsNodeSelected))]
    private async Task RemoveEntry()
    {
        EntryInfoNode? node = selectedNode;

        if (rootStorage is null || node?.Parent is null)
            return;

        try
        {
            bool confirmed = await dialogService.ShowConfirmDialogAsync(
                "Remove Entry",
                $"Are you sure you want to remove '{node.Name}'?");

            if (!confirmed)
                return;

            string entryName = node.EntryInfo.Name;
            Storage parent = node.Parent!.Storage!;

            // Dispose current stream if it's from this node
            if (currentStream is not null)
            {
                currentStream.Dispose();
                currentStream = null;
                HexDocument = null;
            }

            parent.Delete(entryName);
            node.Parent!.RemoveChild(entryName);
            await SetSelectedNodeAsync(node.Parent);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error removing entry: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsStreamSelected))]
    private async Task ImportData()
    {
        if (rootStorage is null || selectedNode is null || selectedNode.Parent is null)
            return;

        try
        {
            IStorageFile? file = await filePickerService.PickFileAsync();
            if (file is null)
                return;

            byte[] data = await File.ReadAllBytesAsync(file.Path.LocalPath);

            // Close current stream before writing
            currentStream?.Dispose();
            currentStream = null;
            HexDocument = null;

            using CfbStream stream = selectedNode.Parent!.Storage!.OpenStream(selectedNode.EntryInfo.Name);
            stream.SetLength(0);
            stream.Write(data, 0, data.Length);

            // Reload the hex view
            OpenStreamForNode(selectedNode);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error importing data: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsStreamSelected))]
    private async Task ExportData()
    {
        if (rootStorage is null || selectedNode is null || selectedNode.Parent is null)
            return;

        try
        {
            string suggestedName = selectedNode.SanitizedFileName;
            IStorageFile? file = await filePickerService.PickExportFileAsync(suggestedName);
            if (file is null)
                return;

            string exportPath = file.Path.LocalPath;

            using CfbStream stream = selectedNode.Parent!.Storage!.OpenStream(selectedNode.EntryInfo.Name);
            using FileStream fileStream = File.Open(exportPath, FileMode.Create);
            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error exporting data: {ex.Message}");
        }
    }

    public async Task SetSelectedNodeAsync(EntryInfoNode? node)
    {
        // Apply pending hex changes before switching
        if (currentStream is not null && HexDocument is not null)
            await ApplyHexChangesAsync();

        // Dispose current stream
        currentStream?.Dispose();
        currentStream = null;

        selectedNode = node;

        // Notify computed property changes
        OnPropertyChanged(nameof(IsNodeSelected));
        OnPropertyChanged(nameof(IsStorageSelected));
        OnPropertyChanged(nameof(IsStreamSelected));

        // Notify command CanExecute changes
        AddStorageCommand.NotifyCanExecuteChanged();
        AddStreamCommand.NotifyCanExecuteChanged();
        RemoveEntryCommand.NotifyCanExecuteChanged();
        ImportDataCommand.NotifyCanExecuteChanged();
        ExportDataCommand.NotifyCanExecuteChanged();

        // Clear everything
        EntryProperties.Clear();
        OleProperties.Clear();
        UserDefinedProperties.Clear();
        HexDocument = null;

        if (node is null)
            return;

        UpdateEntryProperties(node.EntryInfo);

        if (!node.IsStorage && node.Parent is not null)
        {
            OpenStreamForNode(node);
        }
    }

    private void OpenStreamForNode(EntryInfoNode node)
    {
        if (node.Parent is null)
            return;

        try
        {
            currentStream = node.Parent!.Storage!.OpenStream(node.EntryInfo.Name);
            byte[] data = new byte[currentStream.Length];
            currentStream.ReadExactly(data);
            HexDocument = new MemoryBinaryDocument(data);

            UpdateOleTab(currentStream);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            currentStream?.Dispose();
            currentStream = null;
            HexDocument = null;
        }
    }

    private async Task ApplyHexChangesAsync()
    {
        if (currentStream is null || HexDocument is null)
            return;

        try
        {
            ulong length = HexDocument.Length;
            byte[] data = new byte[length];
            HexDocument.ReadBytes(0, data);

            currentStream.Seek(0, SeekOrigin.Begin);
            currentStream.SetLength((long)length);
            currentStream.Write(data, 0, data.Length);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or UnauthorizedAccessException)
        {
            await dialogService.ShowErrorDialogAsync($"Error exporting data: {ex.Message}");
        }
    }

    private void UpdateEntryProperties(in EntryInfo entryInfo)
    {
        EntryProperties.Clear();
        EntryProperties.Add(new EntryProperty("Name", entryInfo.FriendlyName()));
        EntryProperties.Add(new EntryProperty("Type", entryInfo.Type.ToString()));
        EntryProperties.Add(new EntryProperty("Size", entryInfo.Length.ToString()));

        if (entryInfo.CLSID != Guid.Empty)
            EntryProperties.Add(new EntryProperty("CLSID", entryInfo.CLSID.ToString()));

        if (!entryInfo.CreationTime.IsFileTimeUtcZero())
            EntryProperties.Add(new EntryProperty("Created", entryInfo.CreationTime.ToString()));

        if (!entryInfo.ModifiedTime.IsFileTimeUtcZero())
            EntryProperties.Add(new EntryProperty("Modified", entryInfo.ModifiedTime.ToString()));
    }

    private void UpdateOleTab(CfbStream stream)
    {
        OleProperties.Clear();
        UserDefinedProperties.Clear();

        string name = stream.EntryInfo.Name;
        if (name is not PropertySetNames.SummaryInformation and not PropertySetNames.DocSummaryInformation and not PropertySetNames.HwpSummaryInformation)
            return;

        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            OlePropertiesContainer container = new(stream);

            foreach (OleProperty p in container.Properties)
            {
                string valueStr = p.Value switch
                {
                    not byte[] and IList list => string.Join(", ", list.Cast<object?>().Select(o => o?.ToString() ?? string.Empty)),
                    _ => p.Value?.ToString() ?? string.Empty,
                };
                OleProperties.Add(new OlePropertyItem(p.PropertyName, p.VTType.ToString(), valueStr));
            }

            if (container.UserDefinedProperties is not null)
            {
                foreach (OleProperty p in container.UserDefinedProperties.Properties)
                {
                    string valueStr = p.Value switch
                    {
                        not byte[] and IList list => string.Join(", ", list.Cast<object?>().Select(o => o?.ToString() ?? string.Empty)),
                        _ => p.Value?.ToString() ?? string.Empty,
                    };
                    UserDefinedProperties.Add(new OlePropertyItem(p.PropertyName, p.VTType.ToString(), valueStr));
                }
            }
        }
        catch
        {
            // OLE parsing failed; leave grids empty
        }
    }

    private async Task RefreshNodesAsync()
    {
        await SetSelectedNodeAsync(null);
        Nodes.Clear();

        if (rootStorage is null)
            return;

        EntryInfoNode root = new(rootStorage.EntryInfo, rootStorage);
        Nodes.Add(root);
        await SetSelectedNodeAsync(root);
    }
}
