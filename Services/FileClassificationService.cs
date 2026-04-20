using System.IO;

namespace Sorter.Services;

public class FileClassificationService : IFileClassificationService
{
    public string DetermineTargetFolder(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png"  => "Images",
            ".gif" or ".webp" or ".webm" => "Images",
            ".mp4" or ".mov"             => "Videos",
            ".txt" or ".pdf"             => "Documents",
            _                            => "Other"
        };
    }
}
