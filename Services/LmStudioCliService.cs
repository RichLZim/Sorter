using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sorter.Services;

/// <summary>
/// Wraps LM Studio CLI operations (install, load, unload, stop).
/// On Windows: powershell.exe — no elevation, graceful error handling.
/// On macOS/Linux: /bin/sh login shell.
/// </summary>
public class LmStudioCliService
{
    // Alphanumeric, hyphen, dot, underscore, slash — covers HuggingFace IDs
    private static readonly Regex ValidModelNameRegex =
        new(@"^[a-zA-Z0-9\-\._/]+$", RegexOptions.Compiled);

    // OnLog removed — was unused (CS0067). Shell output goes to OnError only.
    public event Action<string>? OnError;

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

    /// <summary>
    /// Stops all LM Studio models/server then reloads only the specified model.
    /// Used by the "Limit Server" enforcement feature.
    /// </summary>
    public Task EnforceSingleServerAsync(string modelName)
    {
        ValidateModelName(modelName);
        return RunShellAsync(
            $"lms server stop && lms unload --all && sleep 1 && lms load {modelName} && lms server start");
    }

    private static void ValidateModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !ValidModelNameRegex.IsMatch(modelName))
            throw new ArgumentException(
                $"Invalid model name: '{modelName}'. Allowed: alphanumeric, - . _ /");
    }

    private async Task RunShellAsync(string command)
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psCommand = command
                    .Replace("&&", ";")
                    .Replace("sleep 1", "Start-Sleep -s 1");

                psi = new ProcessStartInfo
                {
                    FileName        = "powershell.exe",
                    Arguments       = $"-NoProfile -Command \"{psCommand}\"",
                    UseShellExecute = true,  // No runas — lms/ollama don't need admin
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
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            OnError?.Invoke("[Shell] Cancelled: UAC elevation was denied.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            OnError?.Invoke("[Shell] Access denied. Try running Sorter as Administrator.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[Shell] Command failed: {ex.Message}");
        }
    }
}
