using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sorter.Services;
using Sorter.ViewModels;
using System;

namespace Sorter;

public partial class App : Application
{
    public static new App Current => (App)Application.Current!;
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // 1. Register ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ConnectionViewModel>();
        services.AddTransient<SortingProgressViewModel>();
        services.AddTransient<ImagePreviewViewModel>();
        services.AddTransient<MainViewModel>();

        // 2. Register Services
        services.AddSingleton<LmStudioCliService>();
        services.AddSingleton<FileSorterService>();

        // FIX: Tell DI exactly how to build LmStudioService using the saved settings!
        services.AddSingleton<LmStudioService>(sp => 
        {
            var settings = sp.GetRequiredService<SettingsViewModel>();
            return new LmStudioService(settings.LmStudioUrl, settings.ModelName);
        });

        Services = services.BuildServiceProvider();

        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Resolve the MainViewModel via DI
                desktop.MainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainViewModel>()
                };
            }
        }
        catch (Exception ex)
        {
            // If the app ever fails to start, it will write the error to your Output window
            System.Diagnostics.Debug.WriteLine($"CRITICAL STARTUP ERROR: {ex.Message}");
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }
}