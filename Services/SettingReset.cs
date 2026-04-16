using System;
using Sorter.Models;

namespace Sorter.Services;

public static class SettingReset
{
    /// <summary>
    /// Resets all application settings to their default values.
    /// </summary>
    public static void Execute()
    {
        try
        {
            SettingsService.Save(new SorterSettings());
        }
        catch (Exception ex)
        {
            throw new Exception($"Critical failure during settings reset: {ex.Message}", ex);
        }
    }
}
