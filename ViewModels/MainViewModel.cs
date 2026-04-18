using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FileSorterService  _sorter;
    private readonly LmStudioCliService _cli;
    private CancellationTokenSource? _cts;

    public IStorageProvider? StorageProvider { get; set; }

    // ── Exposed Sub-ViewModels ───────────────────────────────────────────────
    public SettingsViewModel Settings { get; }
    public ConnectionViewModel Connection { get; }
    public SortingProgressViewModel Progress { get; }
    public ImagePreviewViewModel Preview { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSorting))]
    private bool _isSorting;
    public bool IsNotSorting => !IsSorting;

    public MainViewModel(
        FileSorterService  sorter,
        LmStudioService    lmService,
        LmStudioCliService cli,
        IImageService      imageService)
    {
        _sorter = sorter;
        _cli    = cli;

        Settings = new SettingsViewModel();
        Progress = new SortingProgressViewModel();
        Preview  = new ImagePreviewViewModel();
        Connection = new ConnectionViewModel(lmService)
        {
            LmStudioUrl  = Settings.LmStudioUrl,
            ModelName    = Settings.ModelName,
            OnLogMessage = AddLog,
        };

        // Keep connection strings in sync when user edits settings
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.LmStudioUrl))
                Connection.LmStudioUrl = Settings.LmStudioUrl;
            if (e.PropertyName == nameof(SettingsViewModel.ModelName))
                Connection.ModelName = Settings.ModelName;
        };

        _sorter.OnLog   += msg => Dispatcher.UIThread.Post(() => AddLog(msg));
        _sorter.OnError += msg => Dispatcher.UIThread.Post(() => AddLog($"[ERROR] {msg}"));
    }

    [RelayCommand]
    private async Task StartSortAsync()
    {
        if (IsSorting) return;

        if (string.IsNullOrWhiteSpace(Settings.SortingFolder) ||
            string.IsNullOrWhiteSpace(Settings.SortedFolder))
        {
            AddLog("[WARN] Please set both source and output folders.");
            return;
        }

        IsSorting = true;
        Progress.Reset();

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            await _sorter.SortAsync(Settings.SortingFolder, Settings.SortedFolder, _cts.Token);
        }
        finally
        {
            IsSorting = false;
        }
    }

    [RelayCommand]
    private void CancelSort()
    {
        _cts?.Cancel();
        AddLog("Cancellation requested…");
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var folder = await PickFolderAsync();
        if (folder is not null) Settings.SortingFolder = folder;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var folder = await PickFolderAsync();
        if (folder is not null) Settings.SortedFolder = folder;
    }

    [RelayCommand]
    private void ClearLog() => Progress.LogEntries.Clear();

    [RelayCommand]
    private void ResetSettings()
    {
        Settings.ResetSettingsCommand.Execute(null);
        AddLog("Settings reset to defaults.");
    }

    // FIXED: Was targeting TestConnectionAsyncCommand (suffix dropped by generator)
    [RelayCommand]
    private Task TestConnectionAsync() =>
        Connection.TestConnectionCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadLmStudioAsync()
    {
        AddLog($"Starting server with model '{Settings.ModelName}'…");
        await _cli.LoadServerAsync(Settings.ModelName);
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        AddLog("Stopping LM Studio server…");
        await _cli.StopServerAsync();
    }

    [RelayCommand]
    private async Task InstallLmStudioAsync()
    {
        AddLog($"Installing model '{Settings.SelectedModel}'…");
        await _cli.InstallModelAsync(Settings.SelectedModel);
    }

    private void AddLog(string message) => Progress.LogEntries.Add(message);

    private async Task<string?> PickFolderAsync()
    {
        if (StorageProvider is null) return null;

        var results = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });

        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }
}