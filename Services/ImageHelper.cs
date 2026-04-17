using System.IO;
using Avalonia.Media.Imaging;

namespace Sorter.Services; // Change "Helpers" to whatever folder you put this in

public static class ImageHelper
{
    public static Bitmap? LoadBitmapSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (!File.Exists(path)) return null;
            
            // Read all bytes first so the file handle is released immediately
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch 
        { 
            return null; 
        }
    }
}