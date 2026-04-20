
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sorter.Services;

public class FileSorterService
{
    private readonly IFileClassificationService _classifier;
    private readonly IFileSystemService         _fileSystem;
    private readonly IAIImageClassifierService  _aiClassifier;

    public event Action<string>? OnLog;
    public event Action<string>? OnError;
    /// <summary>Fired with the full path of each image file just before it is processed.</summary>
    public event Action<string>? OnPreviewImage;

    public FileSorterService(
        IFileClassificationService classifier,
        IFileSystemService         fileSystem,
        IAIImageClassifierService  aiClassifier)
    {
        _classifier   = classifier;
        _fileSystem   = fileSystem;
        _aiClassifier = aiClassifier;
    }

    public async Task SortAsync(
        string            sourceDirectory,
        string            targetDirectory,
        string            prompt,
        SortOptions       options,
        CancellationToken token)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source not found: {sourceDirectory}");

            var normSource = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var normTarget = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (normTarget.StartsWith(normSource + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                OnError?.Invoke("Output folder must not be inside the source folder.");
                return;
            }

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            OnLog?.Invoke($"Found {files.Length} files.");

            for (int i = 0; i < files.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var file = files[i];

                if (DateExtractorService.IsSupportedImage(file))
                    OnPreviewImage?.Invoke(file);

                try
                {
                    await ProcessFileAsync(file, targetDirectory, prompt, options, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    OnError?.Invoke($"[{Path.GetFileName(file)}] {ex.Message}");
                }
            }

            OnLog?.Invoke("Sorting complete.");
        }
        catch (OperationCanceledException) { OnLog?.Invoke("Sorting cancelled."); }
        catch (Exception ex)               { OnError?.Invoke($"Fatal error: {ex.Message}"); }
    }

    // ── Per-file logic ────────────────────────────────────────────────────────

    private async Task ProcessFileAsync(
        string            filePath,
        string            targetDirectory,
        string            prompt,
        SortOptions       options,
        CancellationToken token)
    {
        var isImage = DateExtractorService.IsSupportedImage(filePath);
        var origExt = Path.GetExtension(filePath).ToLowerInvariant();

        // ── 1. AI classification (images only) ───────────────────────────────
        string aiDescription = "";
        string folder;

        if (isImage)
        {
            var ai = await _aiClassifier.ClassifyAsync(
                filePath, prompt, options.MaxTokens, options.Temperature, token);

            aiDescription = ai?.Description ?? "";
            folder = options.UseSubfolders && !string.IsNullOrWhiteSpace(aiDescription)
                ? SanitizeName(aiDescription)
                : _classifier.DetermineTargetFolder(filePath);
        }
        else
        {
            folder = _classifier.DetermineTargetFolder(filePath);
        }

        // ── 2. Build destination directory ───────────────────────────────────
        var destDir = options.UseSubfolders
            ? Path.Combine(targetDirectory, folder)
            : targetDirectory;

        if (!_fileSystem.DirectoryExists(destDir))
            _fileSystem.CreateDirectory(destDir);

        // ── 3. Build file name ────────────────────────────────────────────────
        string baseName;

        if (isImage)
        {
            var date = DateExtractorService.GetOldestDate(filePath);

            if (date is null && options.IgnoreNonDatedFiles)
            {
                OnLog?.Invoke($"Skipped (no date): {Path.GetFileName(filePath)}");
                return;
            }

            var dateStr  = date.HasValue ? date.Value.ToString("yyyy.MM.dd") : "0000.00.00";
            var descPart = string.IsNullOrWhiteSpace(aiDescription)
                ? "unknown.image"
                : SanitizeDescription(aiDescription);
            var prefix   = options.UsePrefix && !string.IsNullOrWhiteSpace(options.Prefix)
                ? options.Prefix.Trim() + "."
                : "";

            // Base name WITHOUT extension e.g. "2024.06.15.man.in.car"
            baseName = $"{prefix}{dateStr}.{descPart}";
        }
        else
        {
            baseName = Path.GetFileNameWithoutExtension(filePath);
        }

        // ── 4. Resolve collisions: name.1, name.2, name.3 ───────────────────
        // We embed the counter into the base name so "man.in.car.1.jpg",
        // "man.in.car.2.jpg" etc. — never overwriting or losing files.
        var destPath = ResolveUniqueDestination(destDir, baseName, origExt);

        // ── 5. Optionally strip EXIF — write to temp then atomically replace ─
        if (isImage && options.EraseExif)
            TryEraseExif(filePath);

        // ── 6. Move ──────────────────────────────────────────────────────────
        // Safety check: never overwrite. MoveFileAsync also has a fallback but
        // this makes the intent explicit and logs clearly.
        if (File.Exists(destPath))
        {
            OnError?.Invoke(
                $"[SAFETY] Destination already exists and collision resolver failed for " +
                $"'{Path.GetFileName(destPath)}'. File skipped.");
            return;
        }

        await _fileSystem.MoveFileAsync(filePath, destPath);
        OnLog?.Invoke(
            $"Moved: {Path.GetFileName(filePath)} → " +
            $"{(options.UseSubfolders ? folder + "/" : "")}{Path.GetFileName(destPath)}");
    }

    // ── Collision resolver ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a path guaranteed not to exist.
    /// First try: baseName + ext  → e.g. "2024.06.15.man.in.car.jpg"
    /// Subsequent: baseName.N + ext → "2024.06.15.man.in.car.1.jpg", ".2", ".3" …
    /// </summary>
    private static string ResolveUniqueDestination(string dir, string baseName, string ext)
    {
        var first = Path.Combine(dir, baseName + ext);
        if (!File.Exists(first))
            return first;

        for (int n = 1; n < 10_000; n++)
        {
            var candidate = Path.Combine(dir, $"{baseName}.{n}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        // Absolute fallback — append a GUID segment (should never happen in practice)
        return Path.Combine(dir, $"{baseName}.{Guid.NewGuid():N}{ext}");
    }

    // ── EXIF stripping ───────────────────────────────────────────────────────

    private void TryEraseExif(string filePath)
    {
       var tmp = filePath + ".sorter_tmp";
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg") return;

            var original = File.ReadAllBytes(filePath);
            var cleaned  = StripJpegApp1(original);
            if (cleaned is null || cleaned.Length == 0) return;

            // Write to temp file first, then atomically move
            File.WriteAllBytes(tmp, cleaned);
            
            // File.Move with overwrite=true is much safer on Mac/Linux than File.Replace
            File.Move(tmp, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Now the user actually knows if EXIF stripping failed for a file!
            OnError?.Invoke($"[EXIF] Failed to strip EXIF from {Path.GetFileName(filePath)}: {ex.Message}");
            
            // Cleanup the temporary file so we don't litter the user's hard drive
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { /* Ignore cleanup failures */ }
            }
        }
    }

    private static byte[]? StripJpegApp1(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return null;

        using var input  = new MemoryStream(data);
        using var output = new MemoryStream(data.Length);
        using var reader = new BinaryReader(input);
        using var writer = new BinaryWriter(output);

        writer.Write((byte)0xFF);
        writer.Write((byte)0xD8);
        input.Position = 2;

        while (input.Position < input.Length - 1)
        {
            if (reader.ReadByte() != 0xFF) break;
            var marker = reader.ReadByte();

            if (marker == 0xD8 || marker == 0xD9)
            {
                writer.Write((byte)0xFF);
                writer.Write(marker);
                continue;
            }

            if (marker == 0xDA) // SOS — compressed data, copy rest to end
            {
                writer.Write((byte)0xFF);
                writer.Write(marker);
                writer.Write(reader.ReadBytes((int)(input.Length - input.Position)));
                break;
            }

            var lenHigh   = reader.ReadByte();
            var lenLow    = reader.ReadByte();
            var segLength = (lenHigh << 8) | lenLow;
            var segData   = reader.ReadBytes(segLength - 2);

            if (marker == 0xE1) continue; // APP1 = EXIF — drop it

            writer.Write((byte)0xFF);
            writer.Write(marker);
            writer.Write(lenHigh);
            writer.Write(lenLow);
            writer.Write(segData);
        }

        return output.ToArray();
    }

    // ── Name sanitisers ──────────────────────────────────────────────────────

    private static string SanitizeName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "Other" : name.Trim();
    }

    private static string SanitizeDescription(string desc)
    {
        var clean = desc.Trim().ToLowerInvariant()
                        .Replace(' ', '.').Replace('_', '.');
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"[^a-z0-9\.]", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\.{2,}", ".");
        return string.IsNullOrWhiteSpace(clean.Trim('.')) ? "unknown.image" : clean.Trim('.');
    }
}

/// <summary>Options passed from the UI into each sort run.</summary>
public record SortOptions(
    bool   UseSubfolders,
    bool   UsePrefix,
    string Prefix,
    bool   IgnoreNonDatedFiles,
    bool   EraseExif,
    int    MaxTokens,
    double Temperature);
