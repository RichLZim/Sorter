using Sorter.Services;

namespace Sorter.Models;

public class SorterSettings
{
    // ── Folders ───────────────────────────────────────────────────────────────
    public string SortingFolder       { get; set; } = "";
    public string SortedFolder        { get; set; } = "";

    // ── Backend selection ─────────────────────────────────────────────────────
    public AiBackend ActiveBackend    { get; set; } = AiBackend.Custom;

    // ── LM Studio settings ────────────────────────────────────────────────────
    public string LmStudioUrl         { get; set; } = "http://127.0.0.1:1234";

    // ── Ollama settings ───────────────────────────────────────────────────────
    public string OllamaUrl           { get; set; } = "http://127.0.0.1:11434";

    // ── Model selection ───────────────────────────────────────────────────────
    public string ModelName           { get; set; } = "gemma4:e4b";
    public string CustomModelName     { get; set; } = "";

    // ── Rename options ────────────────────────────────────────────────────────
    public bool   UsePrefix           { get; set; } = false;
    public string Prefix              { get; set; } = "IMG";

    // ── Filter options ────────────────────────────────────────────────────────
    public bool   IgnoreNonDatedFiles { get; set; } = false;
    public bool   UseSubfolders       { get; set; } = true;
    public bool   EraseExif           { get; set; } = false;

    // ── LLM inference options ─────────────────────────────────────────────────
    public bool   UseGpu              { get; set; } = false;
    public int    MaxTokens           { get; set; } = 256;
    public double Temperature         { get; set; } = 0.2;
    public bool   LimitServer         { get; set; } = false; // <-- ADDED THIS

    // ── Prompt settings ───────────────────────────────────────────────────────
    public bool   UseCustomPrompt     { get; set; } = false;
    public string CustomPrompt        { get; set; } = "";
    public bool   UseVrcPreset        { get; set; } = false;

    // ── Display ───────────────────────────────────────────────────────────────
    public bool   ShowTokenCost       { get; set; } = true;
}

public class FileProcessResult
{
    public string OriginalPath    { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public string NewFileName     { get; set; } = "";
    public string Category        { get; set; } = "";
    public string Status          { get; set; } = "";
    public bool   Success         { get; set; }
    public string Error           { get; set; } = "";
}

public class TokenStats
{
    public int    InputTokens     { get; set; }
    public int    OutputTokens    { get; set; }
    public double TokensPerSecond { get; set; }
    public int    TotalProcessed  { get; set; }
    public int    TotalFiles      { get; set; }
}
