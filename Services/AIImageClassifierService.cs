using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sorter.Services;

public record AiImageClassification(string Description, string FileName);

public interface IAIImageClassifierService
{
    Task<AiImageClassification?> ClassifyAsync(
        string imagePath,
        string prompt,
        int    maxTokens,
        double temperature,
        double topP,
        CancellationToken token);
}

public class AIImageClassifierService : IAIImageClassifierService
{
    private readonly LmStudioService _lm;
    private readonly OllamaService   _ollama;

    public AiBackend ActiveBackend { get; set; } = AiBackend.LmStudio;

    private static readonly Regex MarkdownFenceRegex =
        new(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Compiled);

    public AIImageClassifierService(LmStudioService lm, OllamaService ollama)
    {
        _lm     = lm;
        _ollama = ollama;
    }

    public async Task<AiImageClassification?> ClassifyAsync(
        string imagePath,
        string prompt,
        int    maxTokens,
        double temperature,
        double topP,
        CancellationToken token)
    {
        if (!File.Exists(imagePath)) return null;

        var bytes    = await File.ReadAllBytesAsync(imagePath, token);
        var b64      = Convert.ToBase64String(bytes);
        var mimeType = GetMimeType(imagePath);

        var fullPrompt =
            $"{prompt}\n\n" +
            "Respond with ONLY a JSON object — no markdown, no extra text:\n" +
            "{\"description\": \"three.word.desc\", \"fileName\": \"YYYY.MM.DD.three.word.desc.ext\"}";

        string? rawResponse;

        if (ActiveBackend == AiBackend.Ollama)
        {
            rawResponse = await _ollama.QueryAsync(
                fullPrompt, token,
                imageBase64: b64,
                maxTokens:   maxTokens,
                temperature: temperature,
                topP:        topP);
        }
        else
        {
            rawResponse = await _lm.QueryAsync(
                fullPrompt, token,
                imageBase64:   b64,
                imageMimeType: mimeType,
                maxTokens:     maxTokens,
                temperature:   temperature,
                topP:          topP);
        }

        if (string.IsNullOrWhiteSpace(rawResponse)) return null;

        var messageContent = ExtractMessageContent(rawResponse, ActiveBackend);
        return string.IsNullOrWhiteSpace(messageContent) ? null : ParseClassification(messageContent);
    }

    private static string? ExtractMessageContent(string raw, AiBackend backend)
    {
        try
        {
            using var doc  = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (backend == AiBackend.Ollama)
            {
                if (root.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var c))
                    return c.GetString();
            }
            else
            {
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var c))
                    return c.GetString();
            }
        }
        catch { }
        return raw;
    }

    private static AiImageClassification? ParseClassification(string text)
    {
        var fenceMatch = MarkdownFenceRegex.Match(text);
        var jsonText   = fenceMatch.Success ? fenceMatch.Groups[1].Value.Trim() : text.Trim();

        if (!jsonText.StartsWith('{'))
        {
            var start = jsonText.IndexOf('{');
            var end   = jsonText.LastIndexOf('}');
            if (start >= 0 && end > start)
                jsonText = jsonText[start..(end + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var file = root.TryGetProperty("fileName",    out var f) ? f.GetString() ?? "" : "";
            return string.IsNullOrWhiteSpace(desc) ? null : new AiImageClassification(desc, file);
        }
        catch { return null; }
    }

    private static string GetMimeType(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            _                 => "image/jpeg"
        };
}
