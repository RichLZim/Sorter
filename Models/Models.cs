using System.Text.Json.Serialization;

namespace Sorter.Models;

public class SorterSettings
{
    public bool UsePrefix { get; set; } = false;
    public string Prefix { get; set; } = "IMG";
    public bool IgnoreNonDatedFiles { get; set; } = false;
    public bool ShowTokenCost { get; set; } = true;
    public string LmStudioUrl { get; set; } = "http://127.0.0.1:1234";
    public string ModelName { get; set; } = "gemma-4-26b";
    public string SortingFolder { get; set; } = "";
    public string SortedFolder { get; set; } = "";
}

public class FileProcessResult
{
    public string OriginalPath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public string NewFileName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}

public class TokenStats
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double TokensPerSecond { get; set; }
    public int TotalProcessed { get; set; }
    public int TotalFiles { get; set; }
}
