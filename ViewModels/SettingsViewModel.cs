using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _sortingFolder = "";
    [ObservableProperty] private string _sortedFolder  = "";

    [ObservableProperty] private string _lmStudioUrl   = "http://127.0.0.1:1234";
    [ObservableProperty] private string _modelName     = "gemma-4-26b";
    [ObservableProperty] private string _selectedModel = "gemma-4-26b";
    [ObservableProperty] private bool   _useGpu;

    partial void OnSelectedModelChanged(string value) => ModelName = value;

    [ObservableProperty] private bool   _usePrefix;
    [ObservableProperty] private string _prefix              = "IMG";
    [ObservableProperty] private bool   _ignoreNonDatedFiles;
    [ObservableProperty] private bool   _showTokenCost = true;

    [ObservableProperty] private bool   _useCustomPrompt;
    [ObservableProperty] private string _customPrompt = "";
    [ObservableProperty] private bool   _useVrcPreset;

    private const string DefaultPrompt =
        "Describe this image in exactly three lowercase words separated by dots. " +
        "Only output the three words, nothing else.";

    private const string VrcPresetPrompt =
        "This is a VRChat screenshot. Describe the scene in exactly three lowercase words " +
        "separated by dots (e.g. 'sunset.beach.friends'). Output only the three words.";

    public string ActivePromptText
    {
        get
        {
            if (UseCustomPrompt) return CustomPrompt;
            if (UseVrcPreset)    return VrcPresetPrompt;
            return DefaultPrompt;
        }
        set
        {
            if (UseCustomPrompt) CustomPrompt = value;
        }
    }

    public bool IsDefaultPromptActive => !UseCustomPrompt && !UseVrcPreset;

    partial void OnUseCustomPromptChanged(bool value)
    {
        if (value) UseVrcPreset = false;
        OnPropertyChanged(nameof(ActivePromptText));
        OnPropertyChanged(nameof(IsDefaultPromptActive));
    }

    partial void OnUseVrcPresetChanged(bool value)
    {
        if (value) UseCustomPrompt = false;
        OnPropertyChanged(nameof(ActivePromptText));
        OnPropertyChanged(nameof(IsDefaultPromptActive));
    }

    public string[] AvailableModels { get; } = 
    [
        "gemma-4-26b", "llava-1.5-7b", "llama-3-8b", "mistral-7b", "phi-3-mini"
    ];

    private bool _applyingSettings;

    public SettingsViewModel()
    {
        ApplySettings(SettingsService.Load());

        PropertyChanged += (_, e) =>
        {
            if (!_applyingSettings && e.PropertyName != nameof(SelectedModel) 
                && e.PropertyName != nameof(ActivePromptText) && e.PropertyName != nameof(IsDefaultPromptActive))
            {
                SaveSettings();
            }
        };
    }

    public void ApplySettings(SorterSettings s)
    {
        _applyingSettings = true;
        try
        {
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
            UseCustomPrompt     = s.UseCustomPrompt;
            CustomPrompt        = s.CustomPrompt;
            UseVrcPreset        = s.UseVrcPreset;
        }
        finally
        {
            _applyingSettings = false;
            OnPropertyChanged(nameof(ActivePromptText));
            OnPropertyChanged(nameof(IsDefaultPromptActive));
        }
    }

    public SorterSettings ToModel() => new()
    {
        SortingFolder       = SortingFolder,
        SortedFolder        = SortedFolder,
        LmStudioUrl         = LmStudioUrl,
        ModelName           = ModelName,
        UsePrefix           = UsePrefix,
        Prefix              = Prefix,
        IgnoreNonDatedFiles = IgnoreNonDatedFiles,
        ShowTokenCost       = ShowTokenCost,
        UseGpu              = UseGpu,
        UseCustomPrompt     = UseCustomPrompt,
        CustomPrompt        = CustomPrompt,
        UseVrcPreset        = UseVrcPreset,
    };

    [RelayCommand]
    private void ResetSettings()
    {
        SettingsService.Save(new SorterSettings());
        ApplySettings(SettingsService.Load());
    }

    private void SaveSettings() => SettingsService.Save(ToModel());
}