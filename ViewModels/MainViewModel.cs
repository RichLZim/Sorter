using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly FileSorterService      _sorter;
    private readonly LmStudioCliService     _cli;
    private readonly LmStudioService        _lmService;
    private readonly OllamaService          _ollamaService;
    private readonly AIImageClassifierService _aiClassifier;
    private CancellationTokenSource?        _cts;

    public IStorageProvider?        StorageProvider { get; set; }
    public SettingsViewModel        Settings   { get; }
    public ConnectionViewModel      Connection { get; }
    public SortingProgressViewModel Progress   { get; }
    public ImagePreviewViewModel    Preview    { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSorting))]
    private bool _isSorting;
    public bool IsNotSorting => !IsSorting;

    public MainViewModel(
        FileSorterService         sorter,
        LmStudioService           lmService,
        LmStudioCliService        cli,
        OllamaService             ollamaService,
        AIImageClassifierService  aiClassifier,
        IImageService             imageService)
    {
        _sorter        = sorter;
        _cli           = cli;
        _lmService     = lmService;
        _ollamaService = ollamaService;
        _aiClassifier  = aiClassifier;

        Settings   = new SettingsViewModel();
        Progress   = new SortingProgressViewModel();
        Preview    = new ImagePreviewViewModel();
        Connection = new ConnectionViewModel(lmService)
        {
            LmStudioUrl  = Settings.LmStudioUrl,
            ModelName    = Settings.ModelName,
            OnLogMessage = AddLog,
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.LmStudioUrl))
                Connection.LmStudioUrl = Settings.LmStudioUrl;
            if (e.PropertyName == nameof(SettingsViewModel.ModelName))
                Connection.ModelName = Settings.ModelName;

            // When the user picks a different model, immediately sync it to both backends
            if (e.PropertyName == nameof(SettingsViewModel.SelectedModelEntry)
             || e.PropertyName == nameof(SettingsViewModel.ModelName)
             || e.PropertyName == nameof(SettingsViewModel.CustomModelName))
            {
                _lmService.UpdateSettings(Settings.LmStudioUrl, Settings.ModelName);
                _ollamaService.UpdateSettings(Settings.OllamaUrl, Settings.ModelName);
            }

            // When LimitServer is toggled on, enforce immediately
            if (e.PropertyName == nameof(SettingsViewModel.LimitServer)
                && Settings.LimitServer)
                _ = EnforceSingleServerAsync();
        };

        _sorter.OnLog   += msg => Dispatcher.UIThread.Post(() => AddLog(msg));
        _sorter.OnError += msg => Dispatcher.UIThread.Post(() => AddLog($"[ERROR] {msg}"));

        // Wire CLI error events so denied/access errors surface in the log
        _cli.OnLog   += msg => Dispatcher.UIThread.Post(() => AddLog(msg));
        _cli.OnError += msg => Dispatcher.UIThread.Post(() => AddLog($"[SHELL ERROR] {msg}"));

        // Load image and push it to the preview panel while sorting
        _sorter.OnPreviewImage += path => Dispatcher.UIThread.Post(() =>
        {
            var bmp = imageService.Load(path);
            Preview.UpdateImage(bmp);
        });

        // Detect installed backends on startup (off the UI thread)
        _ = DetectBackendsAsync();
    }

    // ── Backend detection ─────────────────────────────────────────────────────

    private async Task DetectBackendsAsync()
    {
        // Run detection on a thread pool thread — it spawns 'where'/'which'
        var (lms, ollama) = await Task.Run(() =>
            (BackendDetectionService.IsLmStudioInstalled(),
             BackendDetectionService.IsOllamaInstalled()));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Settings.LmStudioDetected = lms;
            Settings.OllamaDetected   = ollama;

            // Auto-select the first detected backend on first run
            // (only if the persisted choice is still "Custom" and something was found)
            if (Settings.ActiveBackend == AiBackend.Custom)
            {
                if (lms)        Settings.ActiveBackend = AiBackend.LmStudio;
                else if (ollama) Settings.ActiveBackend = AiBackend.Ollama;
            }

            AddLog(lms    ? "[INFO] LM Studio installation detected."  : "[INFO] LM Studio not found locally.");
            AddLog(ollama ? "[INFO] Ollama installation detected."      : "[INFO] Ollama not found locally.");
        });
    }

    // ── Backend switch commands ───────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectLmStudioAsync()
    {
        Settings.ActiveBackend = AiBackend.LmStudio;
        _aiClassifier.ActiveBackend = AiBackend.LmStudio;
        _lmService.UpdateSettings(Settings.LmStudioUrl, Settings.ModelName);
        AddLog("[INFO] Switched to LM Studio backend.");

        if (!Settings.LmStudioDetected)
        {
            AddLog("[INFO] LM Studio not found — starting install…");
            try
            {
                await BackendDetectionService.InstallLmStudioAsync();
                await DetectBackendsAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Installation failed to start: {ex.Message}");
            }
        }
    }

   [RelayCommand]
    private async Task SelectOllamaAsync()
    {
        Settings.ActiveBackend = AiBackend.Ollama;
        _aiClassifier.ActiveBackend = AiBackend.Ollama;
        _ollamaService.UpdateSettings(Settings.OllamaUrl, Settings.ModelName);
        AddLog("[INFO] Switched to Ollama backend.");

        if (!Settings.OllamaDetected)
        {
            AddLog("[INFO] Ollama not found — starting install…");
            try
            {
                await BackendDetectionService.InstallOllamaAsync();
                await DetectBackendsAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Installation failed to start: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void SelectCustom()
    {
        Settings.ActiveBackend = AiBackend.Custom;
        _aiClassifier.ActiveBackend = AiBackend.Custom;
        AddLog("[INFO] Switched to Custom backend.");
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

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

        // Sync active backend URL + model before starting
        _lmService.UpdateSettings(Settings.LmStudioUrl, Settings.ModelName);
        _ollamaService.UpdateSettings(Settings.OllamaUrl, Settings.ModelName);
        _aiClassifier.ActiveBackend = Settings.ActiveBackend;

        IsSorting = true;
        Progress.Reset();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var options = new SortOptions(
            UseSubfolders:       Settings.UseSubfolders,
            UsePrefix:           Settings.UsePrefix,
            Prefix:              Settings.Prefix,
            IgnoreNonDatedFiles: Settings.IgnoreNonDatedFiles,
            EraseExif:           Settings.EraseExif,
            MaxTokens:           Settings.MaxTokens,
            Temperature:         Settings.Temperature);

        try
        {
            await _sorter.SortAsync(
                Settings.SortingFolder,
                Settings.SortedFolder,
                Settings.ActivePromptText,
                options,
                _cts.Token);
        }
        finally
        {
            IsSorting = false;
            Preview.UpdateImage(null); // clear preview when done
        }
    }

    [RelayCommand] private void CancelSort() => _cts?.Cancel();

    // ── Folder browsing ───────────────────────────────────────────────────────

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

    // ── Log / settings ────────────────────────────────────────────────────────

    [RelayCommand] private void ClearLog() => Progress.LogEntries.Clear();

    [RelayCommand]
    private void ResetSettings()
    {
        Settings.ResetSettingsCommand.Execute(null);
        AddLog("Settings reset to defaults.");
    }

    /// <summary>Reload settings.json from disk without resetting values.</summary>
    [RelayCommand]
    private void ReloadSettings()
    {
        Settings.ApplySettings(SettingsService.Load());
        AddLog("[INFO] Settings reloaded from disk.");
    }

    /// <summary>Open the AppData/Sorter folder in the system file explorer.</summary>
   [RelayCommand]
    private void OpenSettingsFolder() // FIX: Removed 'static'
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sorter");

        try
        {
            System.IO.Directory.CreateDirectory(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer", $"\"{path}\"") { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", $"\"{path}\"");
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog($"[ERROR] Cannot open folder automatically: {ex.Message}");
        }
    }


    // ── LM Studio CLI ─────────────────────────────────────────────────────────

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

    /// <summary>
    /// Ensures only one LLM server is running.
    /// Stops all LM Studio models and unloads all Ollama models, then
    /// starts only the currently selected model on the currently selected backend.
    /// Runs once when the LimitServer checkbox is checked.
    /// </summary>
    [RelayCommand]
    private async Task EnforceSingleServerAsync()
    {
        AddLog("[LIMIT SERVER] Enforcing single server — stopping all running LLMs…");

        // Stop both backends regardless of which is active
        var model = Settings.ModelName;

        if (Settings.ActiveBackend == AiBackend.LmStudio || Settings.ActiveBackend == AiBackend.Custom)
        {
            AddLog("[LIMIT SERVER] Stopping LM Studio server and unloading all models…");
            await _cli.EnforceSingleServerAsync(
                Settings.ActiveBackend == AiBackend.LmStudio
                    ? SettingsViewModel.LmStudioModelId(Settings.SelectedModelEntry
                        ?? new ModelEntry("", model, ""))
                    : model);
        }

        if (Settings.ActiveBackend == AiBackend.Ollama)
        {
            AddLog("[LIMIT SERVER] Stopping Ollama models and starting selected model…");
            await _ollamaService.EnforceSingleModelAsync(model);
        }

        AddLog($"[LIMIT SERVER] Done. Active model: {model}");
    }
[RelayCommand]
    private async Task InstallLmStudioAsync()
    {
        if (Settings.ActiveBackend == AiBackend.Ollama)
        {
            var tag = Settings.SelectedModelEntry?.ModelId ?? "gemma4:e4b";
            AddLog($"[Ollama] Pulling model '{tag}'…");
            // FIX: Use the injected _ollamaService instance
            await _ollamaService.PullModelAsync(tag); 
        }
        else
        {
            var lmsId = Settings.SelectedModelEntry is { } m
                ? SettingsViewModel.LmStudioModelId(m)
                : Settings.ModelName;
            AddLog($"[LM Studio] Installing '{lmsId}'…");
            await _cli.InstallModelAsync(lmsId);
        }
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

  [RelayCommand]
    private void OpenGitHub() // FIX: Removed 'static'
    {
        const string url = "https://github.com/your-repo/sorter";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddLog($"[ERROR] Cannot open browser: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddLog(string message) => Progress.LogEntries.Add(message);

    private async Task<string?> PickFolderAsync()
    {
        if (StorageProvider is null) return null;
        var results = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });
        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }
}
