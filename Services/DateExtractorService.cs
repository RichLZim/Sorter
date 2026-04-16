using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Sorter.Services;

public static class DateExtractorService
{
    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webm", ".webp"];

    // Matches a 4-digit year in the 2001–2099 range
    private static readonly Regex YearRegex = new(@"20[0-9]{2}", RegexOptions.Compiled);

    public static bool IsSupportedImage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns the oldest date among EXIF date, file creation and file modified.
    /// Returns null if no valid date is found in the 1900–2099 range.
    /// </summary>
    public static DateTime? GetOldestDate(string filePath)
    {
        var candidates = new[]
        {
            TryGetExifDate(filePath),
            TryGetFileDate(filePath, useCreated: true),
            TryGetFileDate(filePath, useCreated: false)
        }
        .Where(d => d.HasValue && d!.Value.Year >= 1900 && d.Value.Year <= 2099)
        .Select(d => d!.Value)
        .ToList();

        return candidates.Count > 0 ? candidates.Min() : null;
    }

    /// <summary>
    /// Returns true if the filename contains a year in the 2001–2099 range.
    /// </summary>
    public static bool FileNameContainsYear(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return YearRegex.IsMatch(name);
    }

    private static DateTime? TryGetExifDate(string filePath)
    {
        try
        {
            // EXIF is only reliable for JPEG / PNG
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return null;

            var directories = ImageMetadataReader.ReadMetadata(filePath);

            foreach (var dir in directories.OfType<ExifSubIfdDirectory>())
            {
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt)) return dt;
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dt2)) return dt2;
            }

            foreach (var dir in directories.OfType<ExifIfd0Directory>())
            {
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt)) return dt;
            }
        }
        catch { }
        return null;
    }

    private static DateTime? TryGetFileDate(string filePath, bool useCreated)
    {
        try
        {
            var info = new FileInfo(filePath);
            return useCreated ? info.CreationTime : info.LastWriteTime;
        }
        catch { }
        return null;
    }
}
