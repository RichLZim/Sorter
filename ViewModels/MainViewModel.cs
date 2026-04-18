using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LmStudioService _lmService;
    private readonly LmStudioCliService _cliService;
    private readonly FileSorterService _sorterService;
    private CancellationTokenSource? _cts;

    public SettingsViewModel Settings { get; }
    public ConnectionViewModel Connection { get; }
    public SortingProgressViewModel Progress { get; }
    public ImagePreviewViewModel Preview { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSortCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSortCommand))]
    private bool _isSorting;

    public IStorageProvider? StorageProvider { get; set; }

    // XAML Pass-through properties
    public static string[] AvailableModels => SettingsViewModel.AvailableModels;
    
    public string SortingFolder { get => Settings.SortingFolder; set => Settings.SortingFolder = value; }
    public string SortedFolder { get => Settings.SortedFolder; set => Settings.SortedFolder = value; }
    public string LmStudioUrl { get => Settings.LmStudioUrl; set => Settings.LmStudioUrl = value; }
    public string ModelName { get => Settings.ModelName; set => Settings.ModelName = value; }
    public string SelectedModel { get => Settings.SelectedModel; set => Settings.SelectedModel = value; }
    public bool UsePrefix { get => Settings.UsePrefix; set => Settings.UsePrefix = value; }
    public string Prefix { get => Settings.Prefix; set => Settings.Prefix = value; }
    public bool IgnoreNonDatedFiles { get => Settings.IgnoreNonDatedFiles; set => Settings.IgnoreNonDatedFiles = value; }
    public bool ShowTokenCost { get => Settings.ShowTokenCost; set => Settings.ShowTokenCost = value; }
    public bool UseGpu { get => Settings.UseGpu; set => Settings.UseGpu = value; }
    
    // Prompt toggles
    public bool UseCustomPrompt { get => Settings.UseCustomPrompt; set => Settings.UseCustomPrompt = value; }
    public bool UseVrcPreset { get => Settings.UseVrcPreset; set => Settings.UseVrcPreset = value; }
    public string CustomPrompt { get => Settings.CustomPrompt; set => Settings.CustomPrompt = value; }
    
    public bool IsDefaultPromptActive => !UseCustomPrompt && !UseVrcPreset;

    public string ActivePromptText
    {
        get
        {
            if (UseVrcPreset) return LmStudioService.VrcPrompt;
            if (!UseCustomPrompt) return LmStudioService.DefaultPrompt;
            return CustomPrompt;
        }
        set
        {
            if (UseCustomPrompt) CustomPrompt = value;
        }
    }

    public double ProgressValue => Progress.ProgressValue;
    public string ProgressText => Progress.ProgressText;
    public string StatusText => Progress.StatusText;
    public string FooterStatusText { get => Progress.FooterStatusText; set => Progress.FooterStatusText = value; }
    public string ResultCountText => Progress.ResultCountText;
    public int InputTokens => Progress.InputTokens;
    public int OutputTokens => Progress.OutputTokens;
    public string TokensPerSecond => Progress.TokensPerSecond;

    public System.Collections.ObjectModel.ObservableCollection<string> LogEntries => Progress.LogEntries;
    public System.Collections.ObjectModel.ObservableCollection<string> ProcessedFiles => Progress.ProcessedFiles;

    public string ConnectionStatusText => Connection.ConnectionStatusText;
    public Avalonia.Media.IBrush ConnectionColor => Connection.ConnectionColor;
    public string ServerButtonText => Connection.ServerButtonText;
    public Avalonia.Media.IBrush ServerButtonBackground => Connection.ServerButtonBackground;
    public System.Windows.Input.ICommand TestConnectionCommand => Connection.TestConnectionCommand;

    public MainViewModel(
        SettingsViewModel settings,
        ConnectionViewModel connection,
        SortingProgressViewModel progress,
        ImagePreviewViewModel preview,
        LmStudioService lmService,
        LmStudioCliService cliService,
        FileSorterService sorterService)
    {
        Settings = settings;
        Connection = connection;
        Progress = progress;
        Preview = preview;
        
        _lmService = lmService;
        _cliService = cliService;
        _sorterService = sorterService;

        // Forward PropertyChanged events so XAML updates seamlessly
        Settings.PropertyChanged += (_, e) => 
        {
            if (e.PropertyName == nameof(Settings.LmStudioUrl)) Connection.LmStudioUrl = Settings.LmStudioUrl;
            if (e.PropertyName == nameof(Settings.ModelName)) Connection.ModelName = Settings.ModelName;
            
            // Trigger UI update for the prompt textbox when settings change
            if (e.PropertyName is nameof(Settings.UseCustomPrompt) or nameof(Settings.UseVrcPreset) or nameof(Settings.CustomPrompt))
            {
                OnPropertyChanged(nameof(ActivePromptText));
                OnPropertyChanged(nameof(IsDefaultPromptActive));
            }
            
            OnPropertyChanged(e.PropertyName);
        };
        Progress.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        Connection.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

        Connection.LmStudioUrl = Settings.LmStudioUrl;
        Connection.ModelName = Settings.ModelName;
        Connection.OnLogMessage = AppendLog;

        _sorterService.LogProgress = new Progress<string>(AppendLog);
        _sorterService.FileProcessedProgress = new Progress<FileProcessResult>(OnFileProcessed);
        _sorterService.TokenStatsProgress = new Progress<TokenStats>(OnTokenStatsUpdated);
    }

    private bool CanStartSort() => !IsSorting;
    private bool CanCancelSort() => IsSorting;

    [RelayCommand(CanExecute = nameof(CanStartSort))]
    private async Task StartSortAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.SortingFolder) || !Directory.Exists(Settings.SortingFolder))
        {
            Progress.FooterStatusText = "⚠ Please select a valid source folder.";
            AppendLog("[ERROR] Invalid or missing source folder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SortedFolder))
        {
            Progress.FooterStatusText = "⚠ Please select an output folder.";
            AppendLog("[ERROR] Output folder not set.");
            return;
        }

        if (Settings.UseVrcPreset)
        {
            _lmService.PromptOverride = LmStudioService.VrcPrompt;
        }
        else if (Settings.UseCustomPrompt && !string.IsNullOrWhiteSpace(Settings.CustomPrompt))
        {
            _lmService.PromptOverride = Settings.CustomPrompt;
        }
        else
        {
            _lmService.PromptOverride = null;
        }

        _lmService.UpdateSettings(Settings.LmStudioUrl, Settings.ModelName);

        IsSorting = true;
        Progress.Reset();
        Progress.FooterStatusText = "Sorting in progress...";
        _cts = new CancellationTokenSource();

        try
        {
            var results = await _sorterService.SortFilesAsync(
                Settings.SortingFolder, Settings.SortedFolder, Settings.ToModel(), _cts.Token);

            int succeeded = 0, failed = 0;
            foreach (var r in results) { if (r.Success) succeeded++; else failed++; }
            Progress.FooterStatusText = $"Done! {succeeded} sorted, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            Progress.FooterStatusText = "Sort cancelled.";
            AppendLog("[CANCEL] Operation cancelled by user.");
        }
        catch (Exception ex)
        {
            Progress.FooterStatusText = $"Error: {ex.Message}";
            AppendLog($"[FATAL] {ex.Message}");
        }
        finally
        {
            IsSorting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSort))]
    private void CancelSort()
    {
        _cts?.Cancel();
        Progress.FooterStatusText = "Cancelling...";
    }

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private void ResetSettings() => Settings.ResetSettingsCommand.Execute(null);

    [RelayCommand]
    private async Task InstallLmStudioAsync()
    {
        AppendLog($"[INFO] Starting LM Studio CLI installation for model: {Settings.ModelName}...");
        try {
            await _cliService.InstallModelAsync(Settings.ModelName);
            AppendLog($"[SUCCESS] Installation/Download process for {Settings.ModelName} finished.");
        } catch (Exception ex) { AppendLog($"[ERROR] Install failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task LoadLmStudioAsync()
    {
        AppendLog($"[INFO] Loading {Settings.ModelName} and starting server...");
        try {
            await _cliService.LoadServerAsync(Settings.ModelName);
            AppendLog("[SUCCESS] LM Studio Server process finished.");
        } catch (Exception ex) { AppendLog($"[ERROR] Server launch failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        AppendLog($"[INFO] Unloading model: {Settings.ModelName}...");
        try {
            await _cliService.UnloadModelAsync(Settings.ModelName);
            AppendLog($"[SUCCESS] Model {Settings.ModelName} unloaded.");
        } catch (Exception ex) { AppendLog($"[ERROR] Stop failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task BrowseSourceAsync() => await BrowseFolderAsync(true);

    [RelayCommand]
    private async Task BrowseOutputAsync() => await BrowseFolderAsync(false);

    private async Task BrowseFolderAsync(bool isSource)
    {
        if (StorageProvider is null)
        {
            AppendLog("[ERROR] StorageProvider not set — cannot open folder picker.");
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = isSource ? "Select Source Folder" : "Select Output Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path is null) return;

        if (isSource) Settings.SortingFolder = path;
        else Settings.SortedFolder = path;
    }

    private void AppendLog(string message) => LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

    private void OnFileProcessed(FileProcessResult result)
    {
        var previewPath = result.Success ? result.DestinationPath : result.OriginalPath;
        var bitmap = ImageHelper.LoadBitmapSafe(previewPath);

        var entry = result.Success
            ? $"✓  [{result.Category}]  {result.NewFileName}"
            : $"✗  {Path.GetFileName(result.OriginalPath)}  — {result.Error}";

        Progress.ProcessedFiles.Add(entry);
        Progress.ResultCountText = $"{Progress.ProcessedFiles.Count} files";

        Preview.UpdateImage(bitmap);
    }

    private void OnTokenStatsUpdated(TokenStats stats)
    {
        Progress.InputTokens = stats.InputTokens;
        Progress.OutputTokens = stats.OutputTokens;
        Progress.TokensPerSecond = stats.TokensPerSecond > 0 ? $"{stats.TokensPerSecond:F1} t/s" : "—";
        Progress.ProgressText = $"{stats.TotalProcessed}/{stats.TotalFiles}";

        if (stats.TotalFiles > 0)
            Progress.ProgressValue = (double)stats.TotalProcessed / stats.TotalFiles * 100;
    }
}