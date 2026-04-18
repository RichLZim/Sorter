using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sorter.Services;

public class LmStudioCliService
{
    // Only allow alphanumeric, dash, and dot characters for model names to prevent command injection
    private static readonly Regex ValidModelNameRegex = new(@"^[a-zA-Z0-9\-\.]+$", RegexOptions.Compiled);

    public async Task InstallModelAsync(string modelName)
    {
        ValidateModelName(modelName);
        await RunPowerShellAsync($"-Command \"npx lmstudio install-cli; lms get {modelName}\"");
    }

    public async Task LoadServerAsync(string modelName)
    {
        ValidateModelName(modelName);
        await RunPowerShellAsync($"-Command \"lms load {modelName}; Start-Sleep -s 1; lms server start\"");
    }

    public async Task UnloadModelAsync(string modelName)
    {
        ValidateModelName(modelName);
        await RunPowerShellAsync($"-Command \"lms unload {modelName}\"");
    }

    private static void ValidateModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !ValidModelNameRegex.IsMatch(modelName))
        {
            throw new ArgumentException($"Invalid model name format: {modelName}. Security exception prevented execution.");
        }
    }

    private async Task RunPowerShellAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Verb = "runas",
            UseShellExecute = true,
            Arguments = arguments
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }
}