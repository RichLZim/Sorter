using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Models;
using Sorter.Services;

namespace Sorter.ViewModels;

/// <summary>Model entry shown in the dropdown.</summary>
public record ModelEntry(string DisplayName, string ModelId, string BackendHint);

public partial class SettingsViewModel : ObservableObject
{
    // ── Folder paths ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _sortingFolder = "";
    [ObservableProperty] private string _sortedFolder  = "";

    // ── Backend selection ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLmStudioSelected))]
    [NotifyPropertyChangedFor(nameof(IsOllamaSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(EffectiveApiUrl))]
    [NotifyPropertyChangedFor(nameof(SelectedModelUrl))]
    private AiBackend _activeBackend = AiBackend.Custom;

    public bool IsLmStudioSelected => ActiveBackend == AiBackend.LmStudio;
    public bool IsOllamaSelected   => ActiveBackend == AiBackend.Ollama;
    public bool IsCustomSelected   => ActiveBackend == AiBackend.Custom;

    // ── Backend detection (set once at startup) ───────────────────────────────
    [ObservableProperty] private bool _lmStudioDetected;
    [ObservableProperty] private bool _ollamaDetected;

    // ── Connection URLs ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveApiUrl))]
    private string _lmStudioUrl = "http://127.0.0.1:1234";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveApiUrl))]
    private string _ollamaUrl = "http://127.0.0.1:11434";

    public string EffectiveApiUrl => ActiveBackend == AiBackend.Ollama ? OllamaUrl : LmStudioUrl;

    // ── Model selection ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomModelSelected))]
    [NotifyPropertyChangedFor(nameof(ModelName))]
    [NotifyPropertyChangedFor(nameof(SelectedModelUrl))]
    private ModelEntry? _selectedModelEntry;

    [ObservableProperty] private string _customModelName = "";

    public bool IsCustomModelSelected => SelectedModelEntry?.ModelId == "Custom";

    public string ModelName =>
        IsCustomModelSelected && !string.IsNullOrWhiteSpace(CustomModelName)
            ? CustomModelName
            : SelectedModelEntry?.ModelId ?? "";

    partial void OnSelectedModelEntryChanged(ModelEntry? value) => OnPropertyChanged(nameof(ModelName));
    partial void OnCustomModelNameChanged(string value)
    {
        if (IsCustomModelSelected)
        {
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedModelUrl));
        }
    }

    // ── Available models ──────────────────────────────────────────────────────
    // ModelId is the Ollama tag (used by Ollama directly, and as the key to look up the LMS id).
    public ModelEntry[] AvailableModels { get; } =
    [
        new("Gemma 4 26B  (\u2248 20 GB VRAM)",    "gemma4:26b",    "both"),
        new("Gemma 4 E4B  (\u2248  8 GB VRAM)",    "gemma4:e4b",    "both"),
        new("Qwen 2.5-VL 7B  (\u2248  6 GB VRAM)", "qwen2.5vl:7b",  "both"),
        new("LLaVA 13B  (\u2248  9 GB VRAM)",      "llava:13b",     "both"),
        new("Custom",                               "Custom",        "any"),
    ];

    // Maps the shared ModelId key → the exact string passed to `lms get <id>` on LM Studio.
    // These are the official Google/provider model IDs on LM Studio's registry.
    public static string LmStudioModelId(ModelEntry m) => m.ModelId switch
    {
        "gemma4:26b"   => "google/gemma-4-26b-a4b",
        "gemma4:e4b"   => "google/gemma-4-e4b",
        "qwen2.5vl:7b" => "lmstudio-community/Qwen2.5-VL-7B-Instruct-GGUF/Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf",
        "llava:13b"    => "cjpais/llava-v1.6-vicuna-13b-gguf/llava-v1.6-vicuna-13b.Q4_K_M.gguf",
        _              => m.ModelId
    };


    // ── Model URL for hover tooltip ───────────────────────────────────────────
    /// <summary>Returns the download/info URL for the currently selected model on the active backend.</summary>
    public string SelectedModelUrl
    {
        get
        {
            if (SelectedModelEntry is null) return "";
            if (IsCustomModelSelected)      return CustomModelName; // show the custom ID they typed
            return ActiveBackend == AiBackend.Ollama
                ? OllamaModelUrl(SelectedModelEntry)
                : LmStudioModelUrl(SelectedModelEntry);
        }
    }

    public static string OllamaModelUrl(ModelEntry m) => m.ModelId switch
    {
        "gemma4:26b"   => "https://ollama.com/library/gemma4",
        "gemma4:e4b"   => "https://ollama.com/library/gemma4",
        "qwen2.5vl:7b" => "https://ollama.com/library/qwen2.5vl",
        "llava:13b"    => "https://ollama.com/library/llava",
        _              => $"https://ollama.com/library/{m.ModelId.Split(':')[0]}",
    };

    public static string LmStudioModelUrl(ModelEntry m) => m.ModelId switch
    {
        "gemma4:26b"   => "https://huggingface.co/google/gemma-4-26b-a4b",
        "gemma4:e4b"   => "https://huggingface.co/google/gemma-4-e4b",
        "qwen2.5vl:7b" => "https://huggingface.co/lmstudio-community/Qwen2.5-VL-7B-Instruct-GGUF",
        "llava:13b"    => "https://huggingface.co/cjpais/llava-v1.6-vicuna-13b-gguf",
        _              => "",
    };

    // ── GPU / inference ───────────────────────────────────────────────────────
    [ObservableProperty] private bool   _useGpu;
    [ObservableProperty] private int    _maxTokens   = 256;
    [ObservableProperty] private double _temperature = 0.2;
    [ObservableProperty] private double _topP        = 0.9;

    // ── Rename / filter options ───────────────────────────────────────────────
    [ObservableProperty] private bool   _usePrefix;
    [ObservableProperty] private string _prefix = "IMG";
    [ObservableProperty] private bool   _ignoreNonDatedFiles;
    [ObservableProperty] private bool   _useSubfolders = true;
    [ObservableProperty] private bool   _eraseExif;
    [ObservableProperty] private bool   _showTokenCost = true;
    [ObservableProperty] private bool   _limitServer;

    // ── Prompt ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _useCustomPrompt;
    [ObservableProperty] private string _customPrompt = "";
    [ObservableProperty] private bool   _useVrcPreset;

    private const string DefaultPrompt =
        "Describe this image in exactly three lowercase words separated by dots. Only output the three words, nothing else.";
    private const string VrcPresetPrompt =
        "This is a VRChat screenshot. Describe the scene in exactly three lowercase words separated by dots (e.g. 'sunset.beach.friends'). Output only the three words.";

    public string ActivePromptText
    {
        get { if (UseCustomPrompt) return CustomPrompt; if (UseVrcPreset) return VrcPresetPrompt; return DefaultPrompt; }
        set { if (UseCustomPrompt) CustomPrompt = value; }
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

    private bool _applyingSettings;

    public SettingsViewModel()
    {
        SelectedModelEntry = AvailableModels[1]; // Default: Gemma 4 E4B (~8 GB)
        ApplySettings(SettingsService.Load());

        PropertyChanged += (_, e) =>
        {
            if (_applyingSettings) return;
            if (e.PropertyName is nameof(ActivePromptText) or nameof(IsDefaultPromptActive)
                or nameof(ModelName) or nameof(EffectiveApiUrl) or nameof(IsCustomModelSelected)
                or nameof(IsLmStudioSelected) or nameof(IsOllamaSelected) or nameof(IsCustomSelected)
                or nameof(LmStudioDetected) or nameof(OllamaDetected)
)
                return;
            SaveSettings();
        };
    }

    public void ApplySettings(SorterSettings s)
    {
        _applyingSettings = true;
        try
        {
            SortingFolder       = s.SortingFolder;
            SortedFolder        = s.SortedFolder;
            ActiveBackend       = s.ActiveBackend;
            LmStudioUrl         = s.LmStudioUrl;
            OllamaUrl           = s.OllamaUrl;
            UsePrefix           = s.UsePrefix;
            Prefix              = s.Prefix;
            IgnoreNonDatedFiles = s.IgnoreNonDatedFiles;
            UseSubfolders       = s.UseSubfolders;
            EraseExif           = s.EraseExif;
            UseGpu              = s.UseGpu;
            MaxTokens           = s.MaxTokens;
            Temperature         = s.Temperature;
            TopP                = s.TopP;
            UseCustomPrompt     = s.UseCustomPrompt;
            CustomPrompt        = s.CustomPrompt;
            UseVrcPreset        = s.UseVrcPreset;
            ShowTokenCost       = s.ShowTokenCost;
            LimitServer         = s.LimitServer;
            CustomModelName     = s.CustomModelName;
            var match = System.Array.Find(AvailableModels, m => m.ModelId == s.ModelName);
            SelectedModelEntry = match ?? AvailableModels[1]; // Fallback: Gemma 4 E4B
        }
        finally
        {
            _applyingSettings = false;
            OnPropertyChanged(nameof(ActivePromptText));
            OnPropertyChanged(nameof(IsDefaultPromptActive));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(EffectiveApiUrl));
            OnPropertyChanged(nameof(SelectedModelUrl));
        }
    }

    public SorterSettings ToModel() => new()
    {
        SortingFolder       = SortingFolder,
        SortedFolder        = SortedFolder,
        ActiveBackend       = ActiveBackend,
        LmStudioUrl         = LmStudioUrl,
        OllamaUrl           = OllamaUrl,
        ModelName           = SelectedModelEntry?.ModelId ?? "",
        CustomModelName     = CustomModelName,
        UsePrefix           = UsePrefix,
        Prefix              = Prefix,
        IgnoreNonDatedFiles = IgnoreNonDatedFiles,
        UseSubfolders       = UseSubfolders,
        EraseExif           = EraseExif,
        UseGpu              = UseGpu,
        MaxTokens           = MaxTokens,
        Temperature         = Temperature,
        TopP                = TopP,
        UseCustomPrompt     = UseCustomPrompt,
        CustomPrompt        = CustomPrompt,
        UseVrcPreset        = UseVrcPreset,
        ShowTokenCost       = ShowTokenCost,
        LimitServer         = LimitServer,
    };

    [RelayCommand]
    private void ResetSettings()
    {
        SettingsService.Save(new SorterSettings());
        ApplySettings(SettingsService.Load());
    }

    private void SaveSettings() => SettingsService.Save(ToModel());
}
