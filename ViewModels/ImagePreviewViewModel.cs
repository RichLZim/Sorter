using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sorter.ViewModels;

public partial class ImagePreviewViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private Bitmap? _currentImageSource;

    public bool HasImage => CurrentImageSource is not null;

    /// <summary>
    /// Swaps in a new bitmap and disposes the previous one to free unmanaged memory.
    /// Pass <c>null</c> to clear the preview.
    /// </summary>
    public void UpdateImage(Bitmap? newImage)
    {
        var old = CurrentImageSource;
        CurrentImageSource = newImage;
        old?.Dispose();
    }

    public void Dispose() => UpdateImage(null);
}