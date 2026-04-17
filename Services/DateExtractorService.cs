using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Sorter.Services;

public static class DateExtractorService
{
    private const int MinYear = 1900;
    private const int MaxYear = 2099;

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
    /// Returns null if no valid date is found in the MinYear–MaxYear range.
    /// </summary>
    public static DateTime? GetOldestDate(string filePath)
    {
        return new[]
        {
            TryGetExifDate(filePath),
            TryGetFileDate(filePath, useCreated: true),
            TryGetFileDate(filePath, useCreated: false)
        }
        .Where(d => d is { } dt && dt.Year >= MinYear && dt.Year <= MaxYear)
        .Min();
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
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return null;

            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Check SubIFD first (more specific), then IFD0 (general)
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd != null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt)) return dt;
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dt2)) return dt2;
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null && ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt3)) return dt3;
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
