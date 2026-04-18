using System.IO;

namespace Sorter.Services;

public class FileClassificationService : IFileClassificationService
{
    public string DetermineTargetFolder(string filePath)
    {
        // Path.GetExtension never throws on a non-null string, so no try/catch needed.
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
