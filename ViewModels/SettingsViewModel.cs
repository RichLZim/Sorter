using System.Windows.Input;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

/// <summary>
/// Single source of truth for all persisted user settings.
/// MainViewModel reads/writes through this class — no duplicate backing fields elsewhere.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    // ── Folder paths ────────────────────────────────────────────────────────
    private string _sortingFolder = "";
    public string SortingFolder
    {
        get => _sortingFolder;
        set { if (RaiseAndSetIfChanged(ref _sortingFolder, value)) SaveSettings(); }
    }

    private string _sortedFolder = "";
    public string SortedFolder
    {
        get => _sortedFolder;
        set { if (RaiseAndSetIfChanged(ref _sortedFolder, value)) SaveSettings(); }
    }

    // ── AI / connection ─────────────────────────────────────────────────────
    private string _lmStudioUrl = "http://127.0.0.1:1234";
    public string LmStudioUrl
    {
        get => _lmStudioUrl;
        set { if (RaiseAndSetIfChanged(ref _lmStudioUrl, value)) SaveSettings(); }
    }

    private string _modelName = "gemma-4-26b";
    public string ModelName
    {
        get => _modelName;
        set { if (RaiseAndSetIfChanged(ref _modelName, value)) SaveSettings(); }
    }

    // SelectedModel drives ModelName; kept separate so the ComboBox has its own
    // binding target and doesn't fight with direct ModelName edits.
    private string _selectedModel = "gemma-4-26b";
    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedModel, value))
                ModelName = value; // SaveSettings called transitively
        }
    }

    // ── Rename options ──────────────────────────────────────────────────────
    private bool _usePrefix;
    public bool UsePrefix
    {
        get => _usePrefix;
        set { if (RaiseAndSetIfChanged(ref _usePrefix, value)) SaveSettings(); }
    }

    private string _prefix = "IMG";
    public string Prefix
    {
        get => _prefix;
        set { if (RaiseAndSetIfChanged(ref _prefix, value)) SaveSettings(); }
    }

    // ── Filter options ──────────────────────────────────────────────────────
    private bool _ignoreNonDatedFiles;
    public bool IgnoreNonDatedFiles
    {
        get => _ignoreNonDatedFiles;
        set { if (RaiseAndSetIfChanged(ref _ignoreNonDatedFiles, value)) SaveSettings(); }
    }

    // ── Display options ─────────────────────────────────────────────────────
    private bool _showTokenCost = true;
    public bool ShowTokenCost
    {
        get => _showTokenCost;
        set { if (RaiseAndSetIfChanged(ref _showTokenCost, value)) SaveSettings(); }
    }

    private bool _useGpu;
    public bool UseGpu
    {
        get => _useGpu;
        set { if (RaiseAndSetIfChanged(ref _useGpu, value)) SaveSettings(); }
    }

    // ── Available models (single definition — removed from MainViewModel) ───
    public static readonly string[] AvailableModels =
    [
        "gemma-4-26b",
        "llava-1.5-7b",
        "llama-3-8b",
        "mistral-7b",
        "phi-3-mini"
    ];

    // ── Commands ────────────────────────────────────────────────────────────
    public ICommand ResetSettingsCommand { get; }

    // ── Construction ────────────────────────────────────────────────────────
    public SettingsViewModel()
    {
        // Seed from disk without triggering SaveSettings on each assignment.
        ApplySettings(SettingsService.Load(), notify: false);
        ResetSettingsCommand = new RelayCommand(ExecuteResetSettings);
    }

    // ── Public helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies a <see cref="SorterSettings"/> snapshot and fires PropertyChanged
    /// for every property so the UI refreshes. Used by reset and external callers.
    /// </summary>
    public void ApplySettings(SorterSettings s, bool notify = true)
    {
        if (notify)
        {
            // Use public setters so RaiseAndSetIfChanged fires correctly.
            SortingFolder       = s.SortingFolder;
            SortedFolder        = s.SortedFolder;
            LmStudioUrl         = s.LmStudioUrl;
            ModelName           = s.ModelName;
            SelectedModel       = s.ModelName;
            UsePrefix           = s.UsePrefix;
            Prefix              = s.Prefix;
            IgnoreNonDatedFiles = s.IgnoreNonDatedFiles;
            ShowTokenCost       = s.ShowTokenCost;
            UseGpu              = s.UseGpu;
        }
        else
        {
            // Seed backing fields directly — no notifications, no disk write.
            _sortingFolder       = s.SortingFolder;
            _sortedFolder        = s.SortedFolder;
            _lmStudioUrl         = s.LmStudioUrl;
            _modelName           = s.ModelName;
            _selectedModel       = s.ModelName;
            _usePrefix           = s.UsePrefix;
            _prefix              = s.Prefix;
            _ignoreNonDatedFiles = s.IgnoreNonDatedFiles;
            _showTokenCost       = s.ShowTokenCost;
            _useGpu              = s.UseGpu;
        }
    }

    /// <summary>Snapshot of current values, ready to pass to services or save.</summary>
    public SorterSettings ToModel() => new()
    {
        SortingFolder       = _sortingFolder,
        SortedFolder        = _sortedFolder,
        LmStudioUrl         = _lmStudioUrl,
        ModelName           = _modelName,
        UsePrefix           = _usePrefix,
        Prefix              = _prefix,
        IgnoreNonDatedFiles = _ignoreNonDatedFiles,
        ShowTokenCost       = _showTokenCost,
        UseGpu              = _useGpu
    };

    // ── Private ──────────────────────────────────────────────────────────────

    private void ExecuteResetSettings()
    {
        SettingReset.Execute();
        // Bug #6 fix: use ApplySettings(notify:true) so public setters are called
        // and RaiseAndSetIfChanged fires with the *new* value, not the same value.
        ApplySettings(SettingsService.Load(), notify: true);
    }

    private void SaveSettings() => SettingsService.Save(ToModel());
}