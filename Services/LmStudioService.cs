using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sorter.Services;

public class LmStudioService : IDisposable
{
    private readonly HttpClient _client;
    private string _baseUrl;
    private string _modelName;

    // When non-null/non-empty, AnalyzeImageAsync uses this instead of the built-in prompt.
    public string? PromptOverride { get; set; }

    // The default prompt, exposed so the UI can display it as placeholder text.
    public const string DefaultPrompt =
        "Analyze this image carefully. Respond with ONLY valid JSON in this exact format, no other text:\n" +
        "{\n" +
        "  \"category\": \"singleword\",\n" +
        "  \"description\": \"three.word.description\"\n" +
        "}\n\n" +
        "Rules:\n" +
        "- category: ONE word describing the main subject/theme (e.g. nature, food, people, animals, vehicles, architecture, sports, art)\n" +
        "- description: EXACTLY three words joined with dots describing the specific scene (e.g. man.with.dog, kids.birthday.party, mountain.snow.sunset)\n" +
        "- Use only lowercase letters and dots\n" +
        "- Be specific but concise";

   // --> ADD THIS NEW PROMPT BLOCK <--
    public const string VrcPrompt =
        "Analyze this VRChat or video game screenshot. Respond with ONLY valid JSON in this exact format, no other text:\n" +
        "{\n" +
        "  \"category\": \"singleword\",\n" +
        "  \"description\": \"three.word.description\"\n" +
        "}\n\n" +
        "Rules:\n" +
        "- category: ONE word describing the main subject (e.g. avatar, mirror, group, world, event, funny, scenic, action)\n" +
        "- description: EXACTLY three words joined with dots describing the scene (e.g. cute.anime.girl, mirror.group.selfie, dark.scifi.world)\n" +
        "- Use only lowercase letters and dots\n" +
        "- Be specific but concise";

    public LmStudioService(string baseUrl, string modelName)
    {
        _baseUrl = SanitizeUrl(baseUrl);
        _modelName = modelName;

        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public void UpdateSettings(string baseUrl, string modelName)
    {
        _baseUrl = SanitizeUrl(baseUrl);
        _modelName = modelName;
    }

    private static string SanitizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^3].TrimEnd('/');
        return url;
    }

    public async Task<(bool IsSuccess, string Message)> TestConnectionAsync()
    {
        try
        {
            var response = await _client.GetAsync($"{_baseUrl}/v1/models");

            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP Error: {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content) || !content.Contains("data"))
                return (false, "Connected, but response was invalid or empty.");

            return (true, "OK");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection Refused: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection Timed Out.");
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    public async Task<ImageAnalysisResult> AnalyzeImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        string base64Image = Convert.ToBase64String(imageBytes);
        string ext = Path.GetExtension(imagePath).ToLower();
        string mimeType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".webm" => "video/webm",
            _ => "image/jpeg"
        };

        string promptText = !string.IsNullOrWhiteSpace(PromptOverride)
            ? PromptOverride
            : DefaultPrompt;

        var requestBody = new
        {
            model = _modelName,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mimeType};base64,{base64Image}" }
                        },
                        new { type = "text", text = promptText }
                    }
                }
            },
            max_tokens = 100,
            temperature = 0.1
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var startTime = DateTime.UtcNow;

        var response = await _client.PostAsync($"{_baseUrl}/v1/chat/completions", content, cancellationToken);
        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JObject.Parse(responseJson);

        var messageContent = responseObj["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
        var inputTokens = responseObj["usage"]?["prompt_tokens"]?.Value<int>() ?? 0;
        var outputTokens = responseObj["usage"]?["completion_tokens"]?.Value<int>() ?? 0;
        var totalTokens = inputTokens + outputTokens;

        string category = "unsorted";
        string description = "unknown.image.file";

        try
        {
            string cleanContent = messageContent;

            // Safely extract JSON between { and } ignoring markdown wrappers like ```json
            int startIdx = messageContent.IndexOf('{');
            int endIdx = messageContent.LastIndexOf('}');

            if (startIdx >= 0 && endIdx > startIdx)
            {
                cleanContent = messageContent.Substring(startIdx, (endIdx - startIdx) + 1);
            }

            var parsed = JObject.Parse(cleanContent);
            category = SanitizeSingleWord(parsed["category"]?.ToString() ?? "unsorted");
            description = SanitizeThreeWords(parsed["description"]?.ToString() ?? "unknown.image.file");
        }
        catch
        {
            category = "unsorted";
            description = "unrecognized.image.file";
        }

        return new ImageAnalysisResult
        {
            Category = category,
            Description = description,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TokensPerSecond = elapsed > 0 ? totalTokens / elapsed : 0
        };
    }

    private static string SanitizeSingleWord(string input)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(
            input.ToLower().Trim(), @"[^a-z0-9]", "");
        return string.IsNullOrEmpty(clean) ? "unsorted" : clean[..Math.Min(clean.Length, 20)];
    }

    private static string SanitizeThreeWords(string input)
    {
        var parts = input.ToLower().Trim().Split(
            new[] { '.', ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new System.Collections.Generic.List<string>();
        foreach (var p in parts)
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(p, @"[^a-z0-9]", "");
            if (!string.IsNullOrEmpty(clean)) cleanParts.Add(clean);
            if (cleanParts.Count == 3) break;
        }
        while (cleanParts.Count < 3) cleanParts.Add("file");
        return string.Join(".", cleanParts);
    }

    public void Dispose() => _client.Dispose();
}

public class ImageAnalysisResult
{
    public string Category { get; set; } = "unsorted";
    public string Description { get; set; } = "unknown.image.file";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double TokensPerSecond { get; set; }
}