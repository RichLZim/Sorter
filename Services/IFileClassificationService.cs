namespace Sorter.Services;

public interface IFileClassificationService
{
    string DetermineTargetFolder(string filePath);
}
