using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

/// <summary>
/// Orchestrates the application. Owns no duplicated settings state —
/// all persisted values live in <see cref="Settings"/>.
/// Runtime progress lives in <see cref="Progress"/>.
/// Connection state lives in <see cref="Connection"/>.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly LmStudioService    _lmService;
    private readonly LmStudioCliService _cliService;
    private readonly FileSorterService  _sorterService;
    private CancellationTokenSource?    _cts;

    // ── Sub-ViewModels (each owns a clearly defined slice of state) ─────────
    public SettingsViewModel        Settings   { get; }
    public ConnectionViewModel      Connection { get; }
    public SortingProgressViewModel Progress   { get; }
    public ImagePreviewViewModel    Preview    { get; }

    // ── Pass-through properties so XAML bindings keep their existing paths ──
    // These delegate to Settings, which is the single source of truth.

    public string SortingFolder
    {
        get => Settings.SortingFolder;
        set { if (Settings.SortingFolder != value) { Settings.SortingFolder = value; OnSettingChanged(); } }
    }

    public string SortedFolder
    {
        get => Settings.SortedFolder;
        set { if (Settings.SortedFolder != value) { Settings.SortedFolder = value; OnSettingChanged(); } }
    }

    public string LmStudioUrl
    {
        get => Settings.LmStudioUrl;
        set
        {
            if (Settings.LmStudioUrl != value)
            {
                Settings.LmStudioUrl = value;
                Connection.LmStudioUrl = value; // keep connection VM in sync
            }
        }
    }

    public string ModelName
    {
        get => Settings.ModelName;
        set
        {
            if (Settings.ModelName != value)
            {
                Settings.ModelName = value;
                Connection.ModelName = value; // keep connection VM in sync
            }
        }
    }

    public string SelectedModel
    {
        get => Settings.SelectedModel;
        set { if (Settings.SelectedModel != value) Settings.SelectedModel = value; }
    }

    public bool UsePrefix
    {
        get => Settings.UsePrefix;
        set { if (Settings.UsePrefix != value) Settings.UsePrefix = value; }
    }

    public string Prefix
    {
        get => Settings.Prefix;
        set { if (Settings.Prefix != value) Settings.Prefix = value; }
    }

    public bool IgnoreNonDatedFiles
    {
        get => Settings.IgnoreNonDatedFiles;
        set { if (Settings.IgnoreNonDatedFiles != value) Settings.IgnoreNonDatedFiles = value; }
    }

    public bool ShowTokenCost
    {
        get => Settings.ShowTokenCost;
        set { if (Settings.ShowTokenCost != value) Settings.ShowTokenCost = value; }
    }

    public bool UseGpu
    {
        get => Settings.UseGpu;
        set { if (Settings.UseGpu != value) Settings.UseGpu = value; }
    }

    // AvailableModels: single definition lives in SettingsViewModel.
    public static string[] AvailableModels => SettingsViewModel.AvailableModels;

    // ── Execution state (not persisted, lives here) ──────────────────────────
    private bool _isSorting;
    public bool IsSorting
    {
        get => _isSorting;
        set
        {
            if (RaiseAndSetIfChanged(ref _isSorting, value))
            {
                StartSortCommand.RaiseCanExecuteChanged();
                CancelSortCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // Progress/log forwarding — bindings use flat paths, Progress owns the data.
    public double   ProgressValue   => Progress.ProgressValue;
    public string   ProgressText    => Progress.ProgressText;
    public string   StatusText      => Progress.StatusText;
    public string   FooterStatusText
    {
        get => Progress.FooterStatusText;
        set => Progress.FooterStatusText = value;
    }
    public string   ResultCountText => Progress.ResultCountText;
    public int      InputTokens     => Progress.InputTokens;
    public int      OutputTokens    => Progress.OutputTokens;
    public string   TokensPerSecond => Progress.TokensPerSecond;

    public System.Collections.ObjectModel.ObservableCollection<string> LogEntries
        => Progress.LogEntries;
    public System.Collections.ObjectModel.ObservableCollection<string> ProcessedFiles
        => Progress.ProcessedFiles;

    // Connection forwarding
    public string       ConnectionStatusText => Connection.ConnectionStatusText;
    public Avalonia.Media.IBrush ConnectionColor => Connection.ConnectionColor;

    // ── Commands ─────────────────────────────────────────────────────────────
    public RelayCommand StartSortCommand       { get; }
    public RelayCommand CancelSortCommand      { get; }
    public ICommand     TestConnectionCommand  => Connection.TestConnectionCommand;
    public ICommand     ClearLogCommand        { get; }
    public ICommand     ResetSettingsCommand   => Settings.ResetSettingsCommand;
    public ICommand     InstallLmStudioCommand { get; }
    public ICommand     LoadLmStudioCommand    { get; }
    public ICommand     StopServerCommand      { get; }
    public ICommand     BrowseSourceCommand    { get; }
    public ICommand     BrowseOutputCommand    { get; }

    public IStorageProvider? StorageProvider { get; set; }

    // ── Construction ─────────────────────────────────────────────────────────
    public MainViewModel()
    {
        // 1. SettingsViewModel loads from disk in its own constructor.
        Settings = new SettingsViewModel();

        // 2. Initialize services with the already-loaded values.
        _lmService     = new LmStudioService(Settings.LmStudioUrl, Settings.ModelName);
        _sorterService = new FileSorterService(_lmService);
        _cliService    = new LmStudioCliService();

        // 3. Other sub-ViewModels.
        Progress   = new SortingProgressViewModel();
        Preview    = new ImagePreviewViewModel();
        Connection = new ConnectionViewModel(_lmService, AppendLog)
        {
            LmStudioUrl = Settings.LmStudioUrl,
            ModelName   = Settings.ModelName
        };

        // 4. Forward PropertyChanged from sub-ViewModels so XAML bindings refresh.
        //    When Settings raises a change, MainViewModel re-raises with the same name
        //    so any binding on MainViewModel (e.g. {Binding LmStudioUrl}) also updates.
        Settings.PropertyChanged   += (_, e) => RaisePropertyChanged(e.PropertyName);
        Progress.PropertyChanged   += (_, e) => RaisePropertyChanged(e.PropertyName);
        Connection.PropertyChanged += (_, e) => RaisePropertyChanged(e.PropertyName);

        // 5. Commands.
        StartSortCommand       = new RelayCommand(async () => await ExecuteSortAsync(), () => !IsSorting);
        CancelSortCommand      = new RelayCommand(ExecuteCancel, () => IsSorting);
        ClearLogCommand        = new RelayCommand(() => LogEntries.Clear());
        InstallLmStudioCommand = new RelayCommand(async () => await ExecuteInstallCliAsync());
        LoadLmStudioCommand    = new RelayCommand(async () => await ExecuteLoadServerAsync());
        StopServerCommand      = new RelayCommand(ExecuteStopServer);
        BrowseSourceCommand    = new RelayCommand(async () => await BrowseFolderAsync(isSource: true));
        BrowseOutputCommand    = new RelayCommand(async () => await BrowseFolderAsync(isSource: false));

        // 6. Service event wiring.
        _sorterService.OnLogMessage        += AppendLog;
        _sorterService.OnFileProcessed     += OnFileProcessed;
        _sorterService.OnTokenStatsUpdated += OnTokenStatsUpdated;
    }

    // ── Command Implementations ───────────────────────────────────────────────

    private async Task ExecuteSortAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.SortingFolder) ||
            !Directory.Exists(Settings.SortingFolder))
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

        _lmService.UpdateSettings(Settings.LmStudioUrl, Settings.ModelName);

        IsSorting = true;
        Progress.Reset();
        Progress.FooterStatusText = "Sorting in progress...";
        _cts = new CancellationTokenSource();

        try
        {
            var results = await _sorterService.SortFilesAsync(
                Settings.SortingFolder,
                Settings.SortedFolder,
                Settings.ToModel(),
                _cts.Token);

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

    private void ExecuteCancel()
    {
        _cts?.Cancel();
        Progress.FooterStatusText = "Cancelling...";
    }

    private async Task ExecuteInstallCliAsync()
    {
        AppendLog($"[INFO] Starting LM Studio CLI installation for model: {Settings.ModelName}...");
        try
        {
            _cliService.InstallModel(Settings.ModelName);
            AppendLog($"[SUCCESS] Installation/Download process for {Settings.ModelName} finished.");
        }
        catch (Exception ex) { AppendLog($"[ERROR] Install failed: {ex.Message}"); }
    }

    private async Task ExecuteLoadServerAsync()
    {
        AppendLog($"[INFO] Loading {Settings.ModelName} and starting server...");
        try
        {
            await _cliService.LoadServerAsync(Settings.ModelName);
            AppendLog("[SUCCESS] LM Studio Server process finished.");
        }
        catch (Exception ex) { AppendLog($"[ERROR] Server launch failed: {ex.Message}"); }
    }

    private void ExecuteStopServer()
    {
        AppendLog($"[INFO] Unloading model: {Settings.ModelName}...");
        try
        {
            _cliService.UnloadModel(Settings.ModelName);
            AppendLog($"[SUCCESS] Model {Settings.ModelName} unloaded.");
        }
        catch (Exception ex) { AppendLog($"[ERROR] Stop failed: {ex.Message}"); }
    }

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

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void AppendLog(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
    }

    private void OnFileProcessed(FileProcessResult result)
    {
        var previewPath = result.Success ? result.DestinationPath : result.OriginalPath;
        var bitmap = ImageHelper.LoadBitmapSafe(previewPath);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var entry = result.Success
                ? $"✓  [{result.Category}]  {result.NewFileName}"
                : $"✗  {Path.GetFileName(result.OriginalPath)}  — {result.Error}";

            Progress.ProcessedFiles.Add(entry);
            Progress.ResultCountText = $"{Progress.ProcessedFiles.Count} files";

            var old = Preview.CurrentImageSource;
            Preview.CurrentImageSource = bitmap;
            Preview.HasImage = bitmap is not null;
            old?.Dispose();
        });
    }

    private void OnTokenStatsUpdated(TokenStats stats)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Progress.InputTokens     = stats.InputTokens;
            Progress.OutputTokens    = stats.OutputTokens;
            Progress.TokensPerSecond = stats.TokensPerSecond > 0
                ? $"{stats.TokensPerSecond:F1} t/s" : "—";
            Progress.ProgressText    = $"{stats.TotalProcessed}/{stats.TotalFiles}";

            if (stats.TotalFiles > 0)
                Progress.ProgressValue = (double)stats.TotalProcessed / stats.TotalFiles * 100;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Called by pass-through setters that don't need special side-effects.</summary>
    private void OnSettingChanged() { /* hook for future cross-cutting concerns */ }

    /// <summary>
    /// Re-raises PropertyChanged under the given name so XAML bindings
    /// on MainViewModel refresh when a sub-ViewModel property changes.
    /// </summary>
  private void RaisePropertyChanged(string? propertyName)
{
    if (propertyName is not null)
        OnPropertyChanged(propertyName);
}
}