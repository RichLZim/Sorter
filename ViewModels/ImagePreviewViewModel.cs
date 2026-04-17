using System;
using Avalonia.Media.Imaging;
using Sorter.ViewModels;

namespace Sorter.ViewModels;

public class ImagePreviewViewModel : ViewModelBase
{
    private Bitmap? _currentImageSource;
    public Bitmap? CurrentImageSource { get => _currentImageSource; set => RaiseAndSetIfChanged(ref _currentImageSource, value); }

    private bool _hasImage;
    public bool HasImage { get => _hasImage; set => RaiseAndSetIfChanged(ref _hasImage, value); }
}
