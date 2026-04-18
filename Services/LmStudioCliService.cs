using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sorter.Services;

/// <summary>
/// Wraps LM Studio CLI operations (install, load, unload, stop).
///
/// On Windows:  runs via powershell.exe with UAC elevation (runas).
/// On macOS/Linux: runs via a login shell (sh -l -c) so PATH is populated
///   the same as an interactive terminal — no elevation is attempted.
///
/// All model names are validated against an allowlist regex before use to
/// prevent shell injection.
/// </summary>
public class LmStudioCliService
{
    // Only alphanumeric, hyphen, and dot — prevents shell injection
    private static readonly Regex ValidModelNameRegex =
        new(@"^[a-zA-Z0-9\-\.]+$", RegexOptions.Compiled);

    public Task InstallModelAsync(string modelName)
    {
        ValidateModelName(modelName);
        return RunShellAsync($"npx lmstudio install-cli && lms get {modelName}");
    }

    public Task LoadServerAsync(string modelName)
    {
        ValidateModelName(modelName);
        return RunShellAsync($"lms load {modelName} && sleep 1 && lms server start");
    }

    public Task UnloadModelAsync(string modelName)
    {
        ValidateModelName(modelName);
        return RunShellAsync($"lms unload {modelName}");
    }

    public Task StopServerAsync()
        => RunShellAsync("lms server stop");

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void ValidateModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !ValidModelNameRegex.IsMatch(modelName))
            throw new ArgumentException(
                $"Invalid model name: '{modelName}'. Only alphanumeric, '-' and '.' are allowed.");
    }

    /// <summary>
    /// Runs a shell command cross-platform.
    /// Windows: powershell.exe with UAC elevation, && replaced with semicolons,
    ///          sleep replaced with Start-Sleep.
    /// Unix: /bin/sh -l -c (login shell so PATH includes nvm/homebrew/etc.)
    /// </summary>
    private static async Task RunShellAsync(string command)
    {
        ProcessStartInfo psi;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Translate Bash idioms to PowerShell
            var psCommand = command
                .Replace("&&", ";")
                .Replace("sleep 1", "Start-Sleep -s 1");

            psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-Command \"{psCommand}\"",
                Verb            = "runas",
                UseShellExecute = true,
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName        = "/bin/sh",
                Arguments       = $"-l -c \"{command}\"",
                UseShellExecute = false,
            };
        }

        using var process = Process.Start(psi);
        if (process is not null)
            await process.WaitForExitAsync();
    }
}
