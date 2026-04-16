using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sorter.Models;

namespace Sorter.Services;

public class FileSorterService
{
    private readonly LmStudioService _lmService;

    public event Action<string>? OnLogMessage;
    public event Action<FileProcessResult>? OnFileProcessed;
    public event Action<TokenStats>? OnTokenStatsUpdated;

    private int _totalInputTokens;
    private int _totalOutputTokens;
    private double _lastTokensPerSecond;
    private int _processedCount;

    public FileSorterService(LmStudioService lmService)
    {
        _lmService = lmService;
    }

    public async Task<List<FileProcessResult>> SortFilesAsync(
        string sortingFolder,
        string sortedFolder,
        SorterSettings settings,
        CancellationToken cancellationToken = default)
    {
        _totalInputTokens = 0;
        _totalOutputTokens = 0;
        _processedCount = 0;

        var results = new List<FileProcessResult>();

        if (!System.IO.Directory.Exists(sortingFolder))
        {
            Log($"[ERROR] Sorting folder not found: {sortingFolder}");
            return results;
        }

        if (!System.IO.Directory.Exists(sortedFolder))
        {
            System.IO.Directory.CreateDirectory(sortedFolder);
            Log($"[CREATED] Output folder: {sortedFolder}");
        }

        // Gather all supported image files recursively
        var allFiles = System.IO.Directory
            .GetFiles(sortingFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => DateExtractorService.IsSupportedImage(f))
            .ToList();

        Log($"[SCAN] Found {allFiles.Count} image files in: {sortingFolder}");

        // Apply "ignore non-dated" filter
        if (settings.IgnoreNonDatedFiles)
        {
            var before = allFiles.Count;
            allFiles = allFiles
                .Where(f => DateExtractorService.FileNameContainsYear(f) ||
                            DateExtractorService.GetOldestDate(f).HasValue)
                .ToList();
            Log($"[FILTER] Skipped {before - allFiles.Count} non-dated files");
        }

        var totalFiles = allFiles.Count;
        Log($"[QUEUE] Processing {totalFiles} files...");

        for (int i = 0; i < allFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = allFiles[i];
            var result = await ProcessSingleFileAsync(filePath, sortedFolder, settings, i + 1, totalFiles, cancellationToken);
            results.Add(result);
            OnFileProcessed?.Invoke(result);

            _processedCount++;
            OnTokenStatsUpdated?.Invoke(new TokenStats
            {
                InputTokens = _totalInputTokens,
                OutputTokens = _totalOutputTokens,
                TokensPerSecond = _lastTokensPerSecond,
                TotalProcessed = _processedCount,
                TotalFiles = totalFiles
            });
        }

        Log($"[DONE] Completed. {results.Count(r => r.Success)} succeeded, {results.Count(r => !r.Success)} failed.");
        return results;
    }

    private async Task<FileProcessResult> ProcessSingleFileAsync(
        string filePath,
        string sortedFolder,
        SorterSettings settings,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        var result = new FileProcessResult { OriginalPath = filePath };
        var fileName = Path.GetFileName(filePath);

        Log($"[{index}/{total}] Analyzing: {fileName}");

        try
        {
            // Get oldest date
            var date = DateExtractorService.GetOldestDate(filePath);
            string dateStr;
            if (date.HasValue)
            {
                dateStr = $"{date.Value.Year}.{date.Value.Month:D2}.{date.Value.Day:D2}";
            }
            else
            {
                dateStr = "0000.00.00";
                Log($"  [WARN] No valid date found for: {fileName}");
            }

            // Analyze image with LLM
            Log($"  [AI] Sending to LM Studio...");
            var analysis = await _lmService.AnalyzeImageAsync(filePath, cancellationToken);

            _totalInputTokens += analysis.InputTokens;
            _totalOutputTokens += analysis.OutputTokens;
            _lastTokensPerSecond = analysis.TokensPerSecond;

            Log($"  [AI] Category: {analysis.Category} | Desc: {analysis.Description} | Tokens: {analysis.InputTokens}in/{analysis.OutputTokens}out @ {analysis.TokensPerSecond:F1} t/s");

            // Build new filename
            var ext = Path.GetExtension(filePath).ToLower();
            // Normalize .webm to .webp for still images (webm is typically video but listed in requirements)
            string newName;
            if (settings.UsePrefix && !string.IsNullOrWhiteSpace(settings.Prefix))
            {
                var prefix = settings.Prefix.Length > 8
                    ? settings.Prefix[..8]
                    : settings.Prefix;
                newName = $"{prefix}.{dateStr}.{analysis.Description}{ext}";
            }
            else
            {
                newName = $"{dateStr}.{analysis.Description}{ext}";
            }

            // Create category subfolder
            var categoryFolder = Path.Combine(sortedFolder, analysis.Category);
            if (!System.IO.Directory.Exists(categoryFolder))
            {
                System.IO.Directory.CreateDirectory(categoryFolder);
            }

            // Handle filename collisions
            var destPath = Path.Combine(categoryFolder, newName);
		destPath = GetUniqueFilePath(destPath);

// Move the file instead of copying it
File.Move(filePath, destPath);

            result.Success = true;
            result.NewFileName = Path.GetFileName(destPath);
            result.DestinationPath = destPath;
            result.Category = analysis.Category;
            result.Status = "OK";

            Log($"  [OK] -> {analysis.Category}/{Path.GetFileName(destPath)}");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Status = "Cancelled";
            result.Error = "Operation was cancelled";
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Status = "Error";
            result.Error = ex.Message;
            Log($"  [ERROR] {fileName}: {ex.Message}");
        }

        return result;
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        int counter = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(dir, $"{nameNoExt}_{counter++}{ext}");
        }
        return path;
    }

    private void Log(string message)
    {
        OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
