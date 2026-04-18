using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sorter.Services;

public class FileSorterService
{
    private readonly IFileClassificationService _classifier;
    private readonly IFileSystemService         _fileSystem;

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public FileSorterService(
        IFileClassificationService classifier,
        IFileSystemService fileSystem)
    {
        _classifier = classifier;
        _fileSystem  = fileSystem;
    }

    public async Task SortAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken token)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source not found: {sourceDirectory}");

            // Guard: if target is inside source, GetFiles(AllDirectories) would pick up
            // files that were just moved there, causing re-processing or infinite growth.
            var normalizedSource = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar);

            if (normalizedTarget.StartsWith(normalizedSource + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                OnError?.Invoke(
                    "Output folder must not be inside the source folder. " +
                    "Please choose a separate output directory.");
                return;
            }

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            OnLog?.Invoke($"Found {files.Length} files.");

            foreach (var file in files)
            {
                // ThrowIfCancellationRequested puts the Task into Canceled state,
                // enabling proper await-chain cleanup in the caller.
                token.ThrowIfCancellationRequested();

                try
                {
                    var folder         = _classifier.DetermineTargetFolder(file);
                    var destinationDir = Path.Combine(targetDirectory, folder);

                    if (!_fileSystem.DirectoryExists(destinationDir))
                        _fileSystem.CreateDirectory(destinationDir);

                    var destinationPath = Path.Combine(destinationDir, Path.GetFileName(file));
                    await _fileSystem.MoveFileAsync(file, destinationPath);

                    OnLog?.Invoke($"Moved: {Path.GetFileName(file)} → {folder}");
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate so the outer handler logs "cancelled"
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"[{Path.GetFileName(file)}] {ex.Message}");
                }
            }

            OnLog?.Invoke("Sorting complete.");
        }
        catch (OperationCanceledException)
        {
            OnLog?.Invoke("Sorting cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Fatal error: {ex.Message}");
        }
    }
}
