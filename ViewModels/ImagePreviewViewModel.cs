using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Sorter.ViewModels;

public partial class ImagePreviewViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private Bitmap? _currentImageSource;

    public bool HasImage => CurrentImageSource is not null;

    // Safe disposal method to prevent unmanaged memory leaks
    public void UpdateImage(Bitmap? newImage)
    {
        var oldImage = CurrentImageSource;
        CurrentImageSource = newImage;
        oldImage?.Dispose(); 
    }

    public void Dispose()
    {
        CurrentImageSource?.Dispose();
    }
}