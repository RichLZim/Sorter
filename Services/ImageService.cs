using System.IO;
using Avalonia.Media.Imaging;

namespace Sorter.Services;

public class ImageService : IImageService
{
    public Bitmap? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch { return null; }
    }
}
