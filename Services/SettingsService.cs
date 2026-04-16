using System;
using System.IO;
using Newtonsoft.Json;
using Sorter.Models;

namespace Sorter.Services;

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sorter", "settings.json");

    public static SorterSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<SorterSettings>(json) ?? new SorterSettings();
            }
        }
        catch { }
        return new SorterSettings();
    }

    public static void Save(SorterSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch { }
    }
}
