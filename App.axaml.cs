using System;
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

        // Infrastructure
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFileClassificationService, FileClassificationService>();
        services.AddSingleton<IImageService, ImageService>();

        // Business logic
        services.AddSingleton<FileSorterService>();
        services.AddSingleton<LmStudioCliService>();
        services.AddHttpClient<LmStudioService>();

        // View models
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
