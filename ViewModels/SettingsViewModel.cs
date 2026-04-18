using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _sortingFolder = "";
    [ObservableProperty] private string _sortedFolder = "";
    [ObservableProperty] private string _lmStudioUrl = "http://127.0.0.1:1234";
    [ObservableProperty] private string _modelName = "gemma-4-26b";
    
    [ObservableProperty] private string _selectedModel = "gemma-4-26b";
    partial void OnSelectedModelChanged(string value) => ModelName = value;

    [ObservableProperty] private bool _usePrefix;
    [ObservableProperty] private string _prefix = "IMG";
    [ObservableProperty] private bool _ignoreNonDatedFiles;
    [ObservableProperty] private bool _showTokenCost = true;
    [ObservableProperty] private bool _useGpu;
    
    // --- Prompt Toggle State ---
    [ObservableProperty] private bool _useCustomPrompt;
    [ObservableProperty] private string _customPrompt = "";
    [ObservableProperty] private bool _useVrcPreset;

    // Mutually exclusive toggles
    partial void OnUseCustomPromptChanged(bool value)
    {
        if (value) UseVrcPreset = false;
    }

    partial void OnUseVrcPresetChanged(bool value)
    {
        if (value) UseCustomPrompt = false;
    }

    public static readonly string[] AvailableModels = 
    [
        "gemma-4-26b", "llava-1.5-7b", "llama-3-8b", "mistral-7b", "phi-3-mini"
    ];

    public SettingsViewModel()
    {
        ApplySettings(SettingsService.Load());

        // Automatically save to disk when any setting (except SelectedModel dropdown) changes
        PropertyChanged += (s, e) => {
            if (e.PropertyName != nameof(SelectedModel)) SaveSettings();
        };
    }

    public void ApplySettings(SorterSettings s)
    {
        SortingFolder = s.SortingFolder;
        SortedFolder = s.SortedFolder;
        LmStudioUrl = s.LmStudioUrl;
        ModelName = s.ModelName;
        SelectedModel = s.ModelName;
        UsePrefix = s.UsePrefix;
        Prefix = s.Prefix;
        IgnoreNonDatedFiles = s.IgnoreNonDatedFiles;
        ShowTokenCost = s.ShowTokenCost;
        UseGpu = s.UseGpu;
        UseCustomPrompt = s.UseCustomPrompt;
        CustomPrompt = s.CustomPrompt;
        UseVrcPreset = s.UseVrcPreset;
    }

    public SorterSettings ToModel() => new()
    {
        SortingFolder = SortingFolder,
        SortedFolder = SortedFolder,
        LmStudioUrl = LmStudioUrl,
        ModelName = ModelName,
        UsePrefix = UsePrefix,
        Prefix = Prefix,
        IgnoreNonDatedFiles = IgnoreNonDatedFiles,
        ShowTokenCost = ShowTokenCost,
        UseGpu = UseGpu,
        UseCustomPrompt = UseCustomPrompt,
        CustomPrompt = CustomPrompt,
        UseVrcPreset = UseVrcPreset
    };

    [RelayCommand]
    private void ResetSettings()
    {
        SettingReset.Execute();
        ApplySettings(SettingsService.Load());
    }

    private void SaveSettings() => SettingsService.Save(ToModel());
}