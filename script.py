from pathlib import Path
base = Path('output/final')
base.mkdir(parents=True, exist_ok=True)
files = {
'output/final/Services/LmStudioService.cs': '''using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Sorter.Services;

public class LmStudioService
{
    private readonly HttpClient _client;
    private string _baseUrl = "http://127.0.0.1:1234";
    private string _model = "";

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public LmStudioService(HttpClient client)
    {
        _client = client;
    }

    public void UpdateSettings(string baseUrl, string model)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:1234" : baseUrl.TrimEnd('/');
        _model = model ?? string.Empty;
    }

    public async Task<(bool IsSuccess, string Message)> TestConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _client.GetAsync($"{_baseUrl}/v1/models", cts.Token);
            return response.IsSuccessStatusCode ? (true, "OK") : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string?> QueryAsync(string prompt, CancellationToken token)
    {
        try
        {
            var payload = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                stream = false
            };

            var json = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"{_baseUrl}/v1/chat/completions", content, token);

            if (!response.IsSuccessStatusCode)
            {
                OnError?.Invoke($"LM Studio HTTP error: {response.StatusCode}");
                return null;
            }

            var result = await response.Content.ReadAsStringAsync(token);
            OnLog?.Invoke("LM Studio response received.");
            return result;
        }
        catch (OperationCanceledException)
        {
            OnLog?.Invoke("LM request cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"LM failure: {ex.Message}");
            return null;
        }
    }
}
''',
'output/final/Services/AIImageClassifierService.cs': '''using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sorter.Services;

public record AiImageClassification(string Category, string Description, string FileName);

public interface IAIImageClassifierService
{
    Task<AiImageClassification?> ClassifyAsync(string imagePath, string prompt, CancellationToken token);
}

public class AIImageClassifierService : IAIImageClassifierService
{
    private readonly LmStudioService _lm;

    public AIImageClassifierService(LmStudioService lm)
    {
        _lm = lm;
    }

    public async Task<AiImageClassification?> ClassifyAsync(string imagePath, string prompt, CancellationToken token)
    {
        if (!File.Exists(imagePath))
            return null;

        var bytes = await File.ReadAllBytesAsync(imagePath, token);
        var b64 = Convert.ToBase64String(bytes);
        var ext = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();

        var visionPrompt = $"{prompt}\n\nReturn STRICT JSON only with keys category, description, fileName.\nDo not include markdown or commentary.\nImage bytes are attached below as base64 text.\nFile extension: {ext}.\nBase64: {b64}";
        var raw = await _lm.QueryAsync(visionPrompt, token);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "Other" : "Other";
            var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var fileName = root.TryGetProperty("fileName", out var f) ? f.GetString() ?? string.Empty : string.Empty;
            return new AiImageClassification(category, description, fileName);
        }
        catch
        {
            return null;
        }
    }
}
''',
'output/final/Services/FileSorterService.cs': '''using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sorter.Services;

public class FileSorterService
{
    private readonly IFileClassificationService _classifier;
    private readonly IFileSystemService _fileSystem;
    private readonly IAIImageClassifierService _aiClassifier;

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public FileSorterService(
        IFileClassificationService classifier,
        IFileSystemService fileSystem,
        IAIImageClassifierService aiClassifier)
    {
        _classifier = classifier;
        _fileSystem = fileSystem;
        _aiClassifier = aiClassifier;
    }

    public async Task SortAsync(string sourceDirectory, string targetDirectory, string prompt, CancellationToken token)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source not found: {sourceDirectory}");

            var normalizedSource = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedTarget.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalizedTarget.StartsWith(normalizedSource + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                OnError?.Invoke("Output folder must not be inside the source folder. Please choose a separate output directory.");
                return;
            }

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            OnLog?.Invoke($"Found {files.Length} files.");

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var folder = _classifier.DetermineTargetFolder(file);
                    var newName = Path.GetFileName(file);

                    if (DateExtractorService.IsSupportedImage(file))
                    {
                        var ai = await _aiClassifier.ClassifyAsync(file, prompt, token);
                        if (!string.IsNullOrWhiteSpace(ai?.Category))
                            folder = SanitizeFolderName(ai.Category);

                        if (!string.IsNullOrWhiteSpace(ai?.FileName))
                            newName = SanitizeFileName(ai.FileName, Path.GetExtension(file));
                    }

                    var destinationDir = Path.Combine(targetDirectory, folder);
                    if (!_fileSystem.DirectoryExists(destinationDir))
                        _fileSystem.CreateDirectory(destinationDir);

                    var destinationPath = Path.Combine(destinationDir, newName);
                    await _fileSystem.MoveFileAsync(file, destinationPath);
                    OnLog?.Invoke($"Moved: {Path.GetFileName(file)} → {folder}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"[{Path.GetFileName(file)}] {ex.Message}");
                }
            }

            OnLog?.Invoke("Sorting complete.");
        }
        catch (OperationCanceledException)
        {
            OnLog?.Invoke("Sorting cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Fatal error: {ex.Message}");
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "Other" : name.Trim();
    }

    private static string SanitizeFileName(string fileName, string fallbackExt)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = fallbackExt;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            name = name.Replace(ch, '_');

        name = string.IsNullOrWhiteSpace(name) ? "image" : name.Trim();
        return name + ext;
    }
}
''',
'output/final/App.axaml.cs': '''using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sorter.Services;
using Sorter.ViewModels;

namespace Sorter;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFileClassificationService, FileClassificationService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<IAIImageClassifierService, AIImageClassifierService>();

        services.AddSingleton<FileSorterService>();
        services.AddSingleton<LmStudioCliService>();
        services.AddHttpClient<LmStudioService>();

        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
''',
'output/final/ViewModels/MainViewModel.cs': '''using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FileSorterService _sorter;
    private readonly LmStudioCliService _cli;
    private CancellationTokenSource? _cts;

    public IStorageProvider? StorageProvider { get; set; }
    public SettingsViewModel Settings { get; }
    public ConnectionViewModel Connection { get; }
    public SortingProgressViewModel Progress { get; }
    public ImagePreviewViewModel Preview { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSorting))]
    private bool _isSorting;

    public bool IsNotSorting => !IsSorting;

    public MainViewModel(FileSorterService sorter, LmStudioService lmService, LmStudioCliService cli, IImageService imageService)
    {
        _sorter = sorter;
        _cli = cli;
        Settings = new SettingsViewModel();
        Progress = new SortingProgressViewModel();
        Preview = new ImagePreviewViewModel();
        Connection = new ConnectionViewModel(lmService)
        {
            LmStudioUrl = Settings.LmStudioUrl,
            ModelName = Settings.ModelName,
            OnLogMessage = AddLog
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.LmStudioUrl))
                Connection.LmStudioUrl = Settings.LmStudioUrl;
            if (e.PropertyName == nameof(SettingsViewModel.ModelName))
                Connection.ModelName = Settings.ModelName;
        };

        _sorter.OnLog += msg => Dispatcher.UIThread.Post(() => AddLog(msg));
        _sorter.OnError += msg => Dispatcher.UIThread.Post(() => AddLog($"[ERROR] {msg}"));
    }

    [RelayCommand]
    private async Task StartSortAsync()
    {
        if (IsSorting)
            return;

        if (string.IsNullOrWhiteSpace(Settings.SortingFolder) || string.IsNullOrWhiteSpace(Settings.SortedFolder))
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
            await _sorter.SortAsync(Settings.SortingFolder, Settings.SortedFolder, Settings.ActivePromptText, _cts.Token);
        }
        finally
        {
            IsSorting = false;
        }
    }

    [RelayCommand]
    private void CancelSort() => _cts?.Cancel();

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
            Settings.SortingFolder = folder;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
            Settings.SortedFolder = folder;
    }

    [RelayCommand]
    private void ClearLog() => Progress.LogEntries.Clear();

    [RelayCommand]
    private void ResetSettings()
    {
        Settings.ResetSettingsCommand.Execute(null);
        AddLog("Settings reset to defaults.");
    }

    [RelayCommand]
    private Task TestConnectionAsync() => Connection.TestConnectionCommand.ExecuteAsync(null);

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
        if (StorageProvider is null)
            return null;

        var results = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }
}
''',
'output/final/Sorter.csproj': '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <AssemblyName>Sorter</AssemblyName>
    <RootNamespace>Sorter</RootNamespace>
  </PropertyGroup>

  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('windows'))">
    <OutputType>WinExe</OutputType>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>icon.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.3" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.3" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.3" />
    <PackageReference Include="MetadataExtractor" Version="2.8.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>

  <ItemGroup Condition="$([MSBuild]::IsOSPlatform('windows'))">
    <Resource Include="icon.ico" />
  </ItemGroup>
</Project>
''',
'output/final/README-notes.txt': '''Replace the corresponding project files with the files in Services/, ViewModels/, App.axaml.cs, and Sorter.csproj. The remaining UI files can stay as-is.'''
}
for path, content in files.items():
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding='utf-8')
print('created', len(files), 'files')