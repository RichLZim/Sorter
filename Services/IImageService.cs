using Avalonia.Media.Imaging;

namespace Sorter.Services;

public interface IImageService
{
    Bitmap? Load(string path);
}
