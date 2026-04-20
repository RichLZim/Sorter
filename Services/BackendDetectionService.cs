using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Sorter.Services;

public enum AiBackend { LmStudio, Ollama, Custom }

public class BackendDetectionService
{
    /// <summary>Returns true if LM Studio appears to be installed on this machine.</summary>
    public static bool IsLmStudioInstalled()
    {
        // Check for the 'lms' CLI on PATH first (fastest, cross-platform)
        if (IsOnPath("lms")) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Common Windows install locations
            var localApp  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return File.Exists(Path.Combine(localApp,    "Programs", "LM Studio", "LM Studio.exe"))
                || File.Exists(Path.Combine(programFiles, "LM Studio", "LM Studio.exe"))
                || Directory.Exists(Path.Combine(localApp, "LM Studio"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Directory.Exists("/Applications/LM Studio.app")
                || File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".lmstudio", "bin", "lms"));
        }

        // Linux: check ~/.lmstudio
        return Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio"));
    }

    /// <summary>Returns true if Ollama appears to be installed on this machine.</summary>
    public static bool IsOllamaInstalled()
    {
        if (IsOnPath("ollama")) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return File.Exists(Path.Combine(localApp,    "Programs", "Ollama", "ollama.exe"))
                || File.Exists(Path.Combine(programFiles, "Ollama",  "ollama.exe"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Directory.Exists("/Applications/Ollama.app")
                || File.Exists("/usr/local/bin/ollama");

        // Linux
        return File.Exists("/usr/bin/ollama") || File.Exists("/usr/local/bin/ollama");
    }

    /// <summary>Installs LM Studio via the official script / installer for the current OS.</summary>
    public static async Task InstallLmStudioAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Download and run the LM Studio Windows installer silently via PowerShell
            await RunShellAsync(
                "powershell",
                "-Command \"" +
                "$url='https://releases.lmstudio.ai/windows/x64/latest/LM-Studio-Setup.exe';" +
                "$out=$env:TEMP+'\\LMStudioSetup.exe';" +
                "Invoke-WebRequest -Uri $url -OutFile $out;" +
                "Start-Process $out -ArgumentList '/S' -Wait\"",
                elevate: true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: open the download page — no silent installer available
            Process.Start(new ProcessStartInfo(
                "https://lmstudio.ai/download") { UseShellExecute = true });
        }
        else
        {
            // Linux: install via the official AppImage installer script
            await RunShellAsync(
                "/bin/sh",
                "-l -c \"curl -fsSL https://lmstudio.ai/install.sh | sh\"",
                elevate: false);
        }
    }

    /// <summary>Installs Ollama via the official script / installer for the current OS.</summary>
    public static async Task InstallOllamaAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await RunShellAsync(
                "powershell",
                "-Command \"" +
                "$url='https://ollama.com/download/OllamaSetup.exe';" +
                "$out=$env:TEMP+'\\OllamaSetup.exe';" +
                "Invoke-WebRequest -Uri $url -OutFile $out;" +
                "Start-Process $out -ArgumentList '/S' -Wait\"",
                elevate: true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: use the official curl installer
            await RunShellAsync(
                "/bin/sh",
                "-l -c \"curl -fsSL https://ollama.com/install.sh | sh\"",
                elevate: false);
        }
        else
        {
            // Linux
            await RunShellAsync(
                "/bin/sh",
                "-l -c \"curl -fsSL https://ollama.com/install.sh | sh\"",
                elevate: false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsOnPath(string executable)
    {
        try
        {
            var psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new ProcessStartInfo("where",  executable) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo("which",  executable) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };

            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task RunShellAsync(string exe, string args, bool elevate)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = exe,
            Arguments       = args,
            UseShellExecute = elevate,
            Verb            = elevate && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "runas" : "",
        };
        using var p = Process.Start(psi);
        if (p is not null) await p.WaitForExitAsync();
    }
}
