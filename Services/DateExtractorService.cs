using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Sorter.Services;

public static class DateExtractorService
{
    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".webm", ".webp" };

    public static bool IsSupportedImage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return ImageExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns the oldest date among EXIF date, file creation, file modified.
    /// Returns null if no valid date found in 2001-2099 range.
    /// </summary>
    public static DateTime? GetOldestDate(string filePath)
    {
        DateTime? exifDate = TryGetExifDate(filePath);
        DateTime? createdDate = TryGetFileDate(filePath, useCreated: true);
        DateTime? modifiedDate = TryGetFileDate(filePath, useCreated: false);

            var candidates = new[] { exifDate, createdDate, modifiedDate }
            .Where(d => d.HasValue && d.Value.Year >= 1900 && d.Value.Year <= 2099)
            .Select(d => d!.Value)
            .ToList();

        return candidates.Count > 0 ? candidates.Min() : null;
    }

    /// <summary>
    /// Checks if filename contains a year in 2001-2099 range.
    /// </summary>
    public static bool FileNameContainsYear(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        for (int year = 2001; year <= 2099; year++)
        {
            if (fileName.Contains(year.ToString()))
                return true;
        }
        return false;
    }

    private static DateTime? TryGetExifDate(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLower();
            // EXIF only available for JPEG/TIFF-based formats
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return null;

            var directories = ImageMetadataReader.ReadMetadata(filePath);

            foreach (var dir in directories.OfType<ExifSubIfdDirectory>())
            {
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                    return dt;
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dt2))
                    return dt2;
            }

            foreach (var dir in directories.OfType<ExifIfd0Directory>())
            {
                if (dir.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt))
                    return dt;
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
