using System;
using System.IO;
using Newtonsoft.Json;
using Sorter.Services;
using Sorter.Models;

namespace Sorter.Utilities; // Or whatever namespace you prefer for helper classes

public static class SettingReset
{
    /// <summary>
    /// Performs a hard reset of the application settings by 
    /// generating a fresh SorterSettings object and saving it via SettingsService.
    /// </summary>
    public static void Execute()
    {
        try
        {
            // 1. Create a new instance of your settings model.
            // This will use the default values you have defined in the SorterSettings class.
            var freshSettings = new SorterSettings();

            // 2. Use the existing SettingsService to overwrite the file on disk.
            SettingsService.Save(freshSettings);
        }
        catch (Exception ex)
        {
            // Since this is a utility, you might want to throw the error 
            // so your GUI can catch it and show a MessageBox to the user.
            throw new Exception($"Critical failure during settings reset: {ex.Message}");
        }
    }
}
