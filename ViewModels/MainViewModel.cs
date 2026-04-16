using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

public class MainViewModel : BindableBase
{
    private readonly LmStudioService _lmService;
    private readonly FileSorterService _sorterService;
    private CancellationTokenSource? _cts;

    // --- 1. Folder & Path Settings ---
    private string _sortingFolder = "";
    public string SortingFolder
    {
        get => _sortingFolder;
        set { RaiseAndSetIfChanged(ref _sortingFolder, value); SaveSettings(); }
    }

    private string _sortedFolder = "";
    public string SortedFolder
    {
        get => _sortedFolder;
        set { RaiseAndSetIfChanged(ref _sortedFolder, value); SaveSettings(); }
    }

    // --- 2. AI / LM Studio Settings ---
    private string _lmStudioUrl = "http://127.0.0.1:1234";
    public string LmStudioUrl
    {
        get => _lmStudioUrl;
        set { RaiseAndSetIfChanged(ref _lmStudioUrl, value); SaveSettings(); }
    }

    private string _modelName = "gemma-4-26b";
    public string ModelName
    {
        get => _modelName;
        set { RaiseAndSetIfChanged(ref _modelName, value); SaveSettings(); }
    }

    // --- 3. Sorting Logic Options ---
    private bool _usePrefix = false;
    public bool UsePrefix
    {
        get => _usePrefix;
        set { RaiseAndSetIfChanged(ref _usePrefix, value); SaveSettings(); }
    }

    private string _prefix = "IMG";
    public string Prefix
    {
        get => _prefix;
        set { RaiseAndSetIfChanged(ref _prefix, value); SaveSettings(); }
    }

    private bool _ignoreNonDatedFiles = false;
    public bool IgnoreNonDatedFiles
    {
        get => _ignoreNonDatedFiles;
        set { RaiseAndSetIfChanged(ref _ignoreNonDatedFiles, value); SaveSettings(); }
    }

    private bool _showTokenCost = true;
    public bool ShowTokenCost
    {
        get => _showTokenCost;
        set { RaiseAndSetIfChanged(ref _showTokenCost, value); SaveSettings(); }
    }

    // --- 4. Execution & Progress State ---
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

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => RaiseAndSetIfChanged(ref _progressValue, value); }

    private string _progressText = "0/0";
    public string ProgressText { get => _progressText; set => RaiseAndSetIfChanged(ref _progressText, value); }

    // --- 5. Status & Feedback (UI Text) ---
    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => RaiseAndSetIfChanged(ref _statusText, value); }

    private string _connectionStatusText = "LM STUDIO: DISCONNECTED";
    public string ConnectionStatusText { get => _connectionStatusText; set => RaiseAndSetIfChanged(ref _connectionStatusText, value); }

    private string _footerStatusText = "Configure folders above and press START SORT";
    public string FooterStatusText { get => _footerStatusText; set => RaiseAndSetIfChanged(ref _footerStatusText, value); }

    private string _resultCountText = "0 files";
    public string ResultCountText { get => _resultCountText; set => RaiseAndSetIfChanged(ref _resultCountText, value); }

    // --- 6. Token Statistics ---
    private int _inputTokens;
    public int InputTokens { get => _inputTokens; set => RaiseAndSetIfChanged(ref _inputTokens, value); }

    private int _outputTokens;
    public int OutputTokens { get => _outputTokens; set => RaiseAndSetIfChanged(ref _outputTokens, value); }

    private string _tokensPerSecond = "—";
    public string TokensPerSecond { get => _tokensPerSecond; set => RaiseAndSetIfChanged(ref _tokensPerSecond, value); }

    // --- 7. Image Preview State ---
    private Bitmap? _currentImageSource;
    public Bitmap? CurrentImageSource { get => _currentImageSource; set => RaiseAndSetIfChanged(ref _currentImageSource, value); }

    private bool _hasImage;
    public bool HasImage { get => _hasImage; set => RaiseAndSetIfChanged(ref _hasImage, value); }

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<string> ProcessedFiles { get; } = new();

    // --- Commands ---
    public RelayCommand StartSortCommand { get; }
    public RelayCommand CancelSortCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand InstallLmStudioCommand { get; }
    public ICommand LoadLmStudioCommand { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    // Injected by the View so the ViewModel can open folder pickers
    public IStorageProvider? StorageProvider { get; set; }

    public MainViewModel()
    {
        // 1. Load initial settings
        var settings = SettingsService.Load();
        // Assign backing fields directly to skip SaveSettings() on initial load
        _sortingFolder = settings.SortingFolder;
        _sortedFolder = settings.SortedFolder;
        _lmStudioUrl = settings.LmStudioUrl;
        _modelName = settings.ModelName;
        _usePrefix = settings.UsePrefix;
        _prefix = settings.Prefix;
        _ignoreNonDatedFiles = settings.IgnoreNonDatedFiles;
        _showTokenCost = settings.ShowTokenCost;

        // 2. Initialize services
        _lmService = new LmStudioService(LmStudioUrl, ModelName);
        _sorterService = new FileSorterService(_lmService);

        // 3. Setup commands
        StartSortCommand = new RelayCommand(async () => await ExecuteSort(), () => !IsSorting);
        CancelSortCommand = new RelayCommand(ExecuteCancel, () => IsSorting);
        TestConnectionCommand = new RelayCommand(async () => await ExecuteTestConnection());
        ClearLogCommand = new RelayCommand(() => LogEntries.Clear());
        ResetSettingsCommand = new RelayCommand(ExecuteResetSettings);
        InstallLmStudioCommand = new RelayCommand(ExecuteInstallCli);
        LoadLmStudioCommand = new RelayCommand(ExecuteLoadServer);
        BrowseSourceCommand = new RelayCommand(async () => await BrowseFolder(isSource: true));
        BrowseOutputCommand = new RelayCommand(async () => await BrowseFolder(isSource: false));

        // 4. Wire up service events
        _sorterService.OnLogMessage += AppendLog;
        _sorterService.OnFileProcessed += OnFileProcessed;
        _sorterService.OnTokenStatsUpdated += OnTokenStatsUpdated;
    }

    // --- Command Implementations ---

    private async Task ExecuteSort()
    {
        if (string.IsNullOrWhiteSpace(SortingFolder) || !Directory.Exists(SortingFolder))
        {
            FooterStatusText = "⚠ Please select a valid source folder.";
            AppendLog("[ERROR] Invalid or missing source folder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SortedFolder))
        {
            FooterStatusText = "⚠ Please select a valid output folder.";
            AppendLog("[ERROR] Output folder not set.");
            return;
        }

        _lmService.UpdateSettings(LmStudioUrl, ModelName);

        IsSorting = true;
        ProcessedFiles.Clear();
        FooterStatusText = "Sorting in progress...";
        _cts = new CancellationTokenSource();

        try
        {
            var currentSettings = new SorterSettings
            {
                SortingFolder = SortingFolder,
                SortedFolder = SortedFolder,
                LmStudioUrl = LmStudioUrl,
                ModelName = ModelName,
                UsePrefix = UsePrefix,
                Prefix = Prefix,
                IgnoreNonDatedFiles = IgnoreNonDatedFiles,
                ShowTokenCost = ShowTokenCost
            };

            var results = await _sorterService.SortFilesAsync(
                currentSettings.SortingFolder,
                currentSettings.SortedFolder,
                currentSettings,
                _cts.Token);

            int succeeded = 0, failed = 0;
            foreach (var r in results) { if (r.Success) succeeded++; else failed++; }
            FooterStatusText = $"Done! {succeeded} sorted, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            FooterStatusText = "Sort cancelled.";
            AppendLog("[CANCEL] Operation cancelled by user.");
        }
        catch (Exception ex)
        {
            FooterStatusText = $"Error: {ex.Message}";
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
        FooterStatusText = "Cancelling...";
    }

    private async Task ExecuteTestConnection()
    {
        ConnectionStatusText = "LM STUDIO: TESTING...";
        _lmService.UpdateSettings(LmStudioUrl, ModelName);

        var result = await _lmService.TestConnectionAsync();
        ConnectionStatusText = result.IsSuccess ? "LM STUDIO: CONNECTED" : "LM STUDIO: UNREACHABLE";
        AppendLog(result.IsSuccess
            ? $"[OK] LM Studio connected at {LmStudioUrl}"
            : $"[ERROR] Connection failed: {result.Message}");
    }

    private void ExecuteResetSettings()
    {
        try
        {
            SettingReset.Execute();
            var fresh = SettingsService.Load();
            SortingFolder = fresh.SortingFolder;
            SortedFolder = fresh.SortedFolder;
            LmStudioUrl = fresh.LmStudioUrl;
            ModelName = fresh.ModelName;
            UsePrefix = fresh.UsePrefix;
            Prefix = fresh.Prefix;
            IgnoreNonDatedFiles = fresh.IgnoreNonDatedFiles;
            ShowTokenCost = fresh.ShowTokenCost;
            AppendLog("[INFO] Settings reset to defaults.");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Reset failed: {ex.Message}");
        }
    }

    private void ExecuteInstallCli()
    {
        AppendLog("[INFO] Starting LM Studio CLI installation...");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Verb = "runas",
                UseShellExecute = true,
                Arguments = "-Command \"npx lmstudio install-cli; lms get gemma-4-26b\""
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
            AppendLog("[SUCCESS] Installation process finished.");
        }
        catch (Exception ex) { AppendLog($"[ERROR] Install failed: {ex.Message}"); }
    }

    private void ExecuteLoadServer()
    {
        AppendLog("[INFO] Loading Gemma-4-26b and starting server...");
        Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Verb = "runas",
                    UseShellExecute = true,
                    Arguments = "-Command \"lms load gemma-4-26b; Start-Sleep -s 1; lms server start\""
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit();
                AppendLog("[SUCCESS] LM Studio Server process finished.");
            }
            catch (Exception ex) { AppendLog($"[ERROR] Server launch failed: {ex.Message}"); }
        });
    }

    private async Task BrowseFolder(bool isSource)
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

        if (isSource)
            SortingFolder = path;
        else
            SortedFolder = path;
    }

    // --- Event Handlers (Called by Services) ---

    private void AppendLog(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
    }

    private void OnFileProcessed(FileProcessResult result)
    {
        // Load the bitmap BEFORE switching to the UI thread so the file read
        // doesn't block the UI. By this point the file has already been moved,
        // so we read from DestinationPath (which is set by FileSorterService).
        Bitmap? bitmap = null;
        var previewPath = result.Success ? result.DestinationPath : result.OriginalPath;
        try
        {
            if (File.Exists(previewPath))
            {
                // Read all bytes first so the file handle is released immediately
                var bytes = File.ReadAllBytes(previewPath);
                using var ms = new MemoryStream(bytes);
                bitmap = new Bitmap(ms);
            }
        }
        catch { /* bitmap stays null */ }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var entry = result.Success
                ? $"✓  [{result.Category}]  {result.NewFileName}"
                : $"✗  {Path.GetFileName(result.OriginalPath)}  — {result.Error}";
            ProcessedFiles.Add(entry);
            ResultCountText = $"{ProcessedFiles.Count} files";

            // Dispose the previous bitmap to free memory
            var old = CurrentImageSource;
            CurrentImageSource = bitmap;
            HasImage = bitmap is not null;
            old?.Dispose();
        });
    }

    private void OnTokenStatsUpdated(TokenStats stats)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InputTokens = stats.InputTokens;
            OutputTokens = stats.OutputTokens;
            TokensPerSecond = stats.TokensPerSecond > 0 ? $"{stats.TokensPerSecond:F1} t/s" : "—";
            ProgressText = $"{stats.TotalProcessed}/{stats.TotalFiles}";

            if (stats.TotalFiles > 0)
                ProgressValue = (double)stats.TotalProcessed / stats.TotalFiles * 100;
        });
    }

    private void SaveSettings()
    {
        SettingsService.Save(new SorterSettings
        {
            SortingFolder = _sortingFolder,
            SortedFolder = _sortedFolder,
            LmStudioUrl = _lmStudioUrl,
            ModelName = _modelName,
            UsePrefix = _usePrefix,
            Prefix = _prefix,
            IgnoreNonDatedFiles = _ignoreNonDatedFiles,
            ShowTokenCost = _showTokenCost
        });
    }
}

// --- MVVM Helpers ---

public class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Returns true if the value actually changed.</summary>
    protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class RelayCommand : ICommand
{
    private readonly Func<Task>? _asyncExecute;
    private readonly Action? _syncExecute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _syncExecute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _asyncExecute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        if (_asyncExecute is not null)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try { await _asyncExecute(); }
            finally { _isExecuting = false; RaiseCanExecuteChanged(); }
        }
        else
        {
            _syncExecute?.Invoke();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
