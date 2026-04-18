using System.Threading.Tasks;

namespace Sorter.Services;

public interface IFileSystemService
{
    Task MoveFileAsync(string source, string destination);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
}
