using System.IO;
using System.Threading.Tasks;

namespace Sorter.Services;

public class FileSystemService : IFileSystemService
{
    public bool FileExists(string path)      => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Task MoveFileAsync(string source, string destination)
    {
        return Task.Run(() =>
        {
            var target = File.Exists(destination)
                ? GetUniqueFilePath(destination)
                : destination;
            File.Move(source, target);
        });
    }

    private static string GetUniqueFilePath(string path)
    {
        var dir  = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(path);
        var ext  = Path.GetExtension(path);
        var i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name}_{i}{ext}"); i++; }
        while (File.Exists(candidate));
        return candidate;
    }
}
