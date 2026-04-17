using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sorter.Services;

public class LmStudioCliService
{
    public void InstallModel(string modelName)
    {
        RunPowerShell($"-Command \"npx lmstudio install-cli; lms get {modelName}\"");
    }

    public Task LoadServerAsync(string modelName)
    {
        return Task.Run(() => RunPowerShell($"-Command \"lms load {modelName}; Start-Sleep -s 1; lms server start\""));
    }

    public void UnloadModel(string modelName)
    {
        RunPowerShell($"-Command \"lms unload \"");
    }

    private void RunPowerShell(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Verb = "runas",
            UseShellExecute = true,
            Arguments = arguments
        };
        Process.Start(psi)?.WaitForExit();
    }
}