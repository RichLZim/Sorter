using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Sorter.Models;
using Sorter.Services;
using Sorter.Utilities;
using System.Collections.Generic;
namespace Sorter.ViewModels;

public class MainViewModel : BindableBase
{
    private readonly LmStudioService _lmService;
    private readonly FileSorterService _sorterService;
    private CancellationTokenSource? _cts;

    // --- Properties ---
// --- 1. Folder & Path Settings ---
private string _sortingFolder = "";
public string SortingFolder { get => _sortingFolder; set => RaiseAndSetIfChanged(ref _sortingFolder, value); }

private string _sortedFolder = "";
public string SortedFolder { get => _sortedFolder; set => RaiseAndSetIfChanged(ref _sortedFolder, value); }

// --- 2. AI / LM Studio Settings ---
private string _lmStudioUrl = "http://127.0.0.1:1234";
public string LmStudioUrl { get => _lmStudioUrl; set => RaiseAndSetIfChanged(ref _lmStudioUrl, value); }

private string _modelName = "gemma-4-26b";
public string ModelName { get => _modelName; set => RaiseAndSetIfChanged(ref _modelName, value); }

// --- 3. Sorting Logic Options ---
private bool _usePrefix = false;
public bool UsePrefix { get => _usePrefix; set => RaiseAndSetIfChanged(ref _usePrefix, value); }

private string _prefix = "IMG";
public string Prefix { get => _prefix; set => RaiseAndSetIfChanged(ref _prefix, value); }

private bool _ignoreNonDatedFiles = false;
public bool IgnoreNonDatedFiles { get => _ignoreNonDatedFiles; set => RaiseAndSetIfChanged(ref _ignoreNonDatedFiles, value); }

private bool _showTokenCost = true;
public bool ShowTokenCost { get => _showTokenCost; set => RaiseAndSetIfChanged(ref _showTokenCost, value); }
public RelayCommand StartSortCommand { get; }
public RelayCommand CancelSortCommand { get; }
// --- 4. Execution & Progress State ---
private bool _isSorting;
public bool IsSorting 
{ 
    get => _isSorting; 
    set 
    {
        // 1. Store the old value to check if a change actually occurred
        bool oldValue = _isSorting;

        // 2. Call the method (which returns void)
        RaiseAndSetIfChanged(ref _isSorting, value);

        // 3. If the new value is different from the old value, trigger the commands
        if (oldValue != value)
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

  
    public ICommand TestConnectionCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand InstallLmStudioCommand { get; }
    public ICommand LoadLmStudioCommand { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    public MainViewModel()
    {
        // 1. Load initial settings
        var settings = SettingsService.Load();
        SortingFolder = settings.SortingFolder;
        SortedFolder = settings.SortedFolder;
        LmStudioUrl = settings.LmStudioUrl;
        ModelName = settings.ModelName;
        UsePrefix = settings.UsePrefix;
        Prefix = settings.Prefix;
        IgnoreNonDatedFiles = settings.IgnoreNonDatedFiles;
        ShowTokenCost = settings.ShowTokenCost;

        // 2. Initialize Services
        _lmService = new LmStudioService(LmStudioUrl, ModelName);
        _sorterService = new FileSorterService(_lmService);

        // 3. Setup Commands
        StartSortCommand = new RelayCommand(async () => await ExecuteSort(), () => !IsSorting);
        CancelSortCommand = new RelayCommand(() => ExecuteCancel(), () => IsSorting);
        TestConnectionCommand = new RelayCommand(async () => await ExecuteTestConnection());
        ClearLogCommand = new RelayCommand(() => LogEntries.Clear());
        ResetSettingsCommand = new RelayCommand(ExecuteResetSettings);
        InstallLmStudioCommand = new RelayCommand(ExecuteInstallCli);
        LoadLmStudioCommand = new RelayCommand(ExecuteLoadServer);
        BrowseSourceCommand = new RelayCommand(async () => await BrowseFolder(true));
        BrowseOutputCommand = new RelayCommand(async () => await BrowseFolder(false));

        // 4. Wire up Service Events
        _sorterService.OnLogMessage += (msg) => AppendLog(msg);
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

        // Update settings in service before starting
        _lmService.UpdateSettings(LmStudioUrl, ModelName);
        
        IsSorting = true;
        ProcessedFiles.Clear();
        FooterStatusText = "Sorting in progress...";
        _cts = new CancellationTokenSource();

        try
        {
            // We need to create a temporary settings object for the service call 
            // that matches what the UI currently shows
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

            FooterStatusText = $"Done! {results.Count(r => r.Success)} sorted, {results.Count(r => !r.Success)} failed.";
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
            // Update properties to trigger UI refresh
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
        // Note: In a real MVVM app, this logic should be in an LmStudioCliService
        // For now, we keep it here to maintain your existing functionality
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
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit();
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
                var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit();
                AppendLog("[SUCCESS] LM Studio Server process finished.");
            }
            catch (Exception ex) { AppendLog($"[ERROR] Server launch failed: {ex.Message}"); }
        });
    }

    private async Task BrowseFolder(bool isSource)
    {
        // This requires access to the Window's StorageProvider. 
        // In a pure MVVM, you'd use a Service, but for this refactor, we'll assume 
        // the command is called from the View or handled via a service.
        // Since we can't easily get StorageProvider from ViewModel without passing it,
        // I will leave a placeholder. In your actual implementation, you might need 
        // to call this from the code-behind or pass the Window reference.
    }

    // --- Event Handlers (Called by Services) ---

    private void AppendLog(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            // Auto-scroll logic is usually handled in the View via a behavior or code-behind
        });
    }

    private void OnFileProcessed(FileProcessResult result)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            // Update Results List
            var entry = result.Success 
                ? $"✓  [{result.Category}]  {result.NewFileName}" 
                : $"✗  {Path.GetFileName(result.OriginalPath)}  — {result.Error}";
            ProcessedFiles.Add(entry);
            ResultCountText = $"{ProcessedFiles.Count} files";

            // Update Image Preview
            try
            {
                if (File.Exists(result.OriginalPath))
                {
                    using var stream = File.OpenRead(result.OriginalPath);
                    CurrentImageSource = new Bitmap(stream);
                    HasImage = true;
                }
                else
                {
                    HasImage = false;
                }
            }
            catch { HasImage = false; }
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
}

// --- MVVM Helpers ---

public class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();

    // In Avalonia, we define the event. 
    // To fix the warning, we must actually use it or provide a way to trigger it.
    public event EventHandler? CanExecuteChanged;

    // Add this helper method so your ViewModel can tell the command to refresh
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
