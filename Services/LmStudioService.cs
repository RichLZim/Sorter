using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Sorter.Services;

public class LmStudioService
{
    private readonly HttpClient _client;
    private string _baseUrl = "http://127.0.0.1:1234";
    private string _model   = "";

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public LmStudioService(HttpClient client)
    {
        _client = client;
    }

    public void UpdateSettings(string baseUrl, string model)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:1234" : baseUrl.TrimEnd('/');
        _model   = model ?? string.Empty;
    }

    public async Task<(bool IsSuccess, string Message)> TestConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response  = await _client.GetAsync($"{_baseUrl}/v1/models", cts.Token);
            return response.IsSuccessStatusCode
                ? (true,  "OK")
                : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) { return (false, "Connection timed out"); }
        catch (HttpRequestException ex)    { return (false, ex.Message); }
        catch (Exception ex)               { return (false, ex.Message); }
    }

    /// <summary>
    /// Sends a vision-capable chat completion request.
    /// <paramref name="imageBase64"/> and <paramref name="imageMimeType"/> are optional;
    /// when provided the image is passed as an image_url content part (OpenAI vision format),
    /// which is what LM Studio expects for vision models (llava, gemma-4, etc.).
    /// When omitted a plain text message is sent instead.
    /// </summary>
    /// <param name="maxTokens">Max tokens to generate. 0 = let the server decide.</param>
    public async Task<string?> QueryAsync(
        string prompt,
        CancellationToken token,
        string? imageBase64     = null,
        string  imageMimeType   = "image/jpeg",
        int     maxTokens       = 0,
        double  temperature     = 0.2)
    {
        try
        {
            object messageContent;

            if (!string.IsNullOrEmpty(imageBase64))
            {
                // Vision format: content is an array of parts.
                // The image_url part carries the base64 data URI.
                messageContent = new object[]
                {
                    new
                    {
                        type      = "image_url",
                        image_url = new { url = $"data:{imageMimeType};base64,{imageBase64}" }
                    },
                    new { type = "text", text = prompt }
                };
            }
            else
            {
                // Text-only message.
                messageContent = prompt;
            }

            // Build payload — only include max_tokens when the caller sets it,
            // so we don't override the model's own defaults unnecessarily.
            var payloadDict = new System.Collections.Generic.Dictionary<string, object>
            {
                ["model"]       = _model,
                ["messages"]    = new[] { new { role = "user", content = messageContent } },
                ["temperature"] = temperature,
                ["stream"]      = false,
            };
            if (maxTokens > 0)
                payloadDict["max_tokens"] = maxTokens;

            var json    = JsonConvert.SerializeObject(payloadDict);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"{_baseUrl}/v1/chat/completions", content, token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(token);
                OnError?.Invoke($"LM Studio HTTP {(int)response.StatusCode}: {errorBody}");
                return null;
            }

            var result = await response.Content.ReadAsStringAsync(token);
            OnLog?.Invoke("LM Studio response received.");
            return result;
        }
        catch (OperationCanceledException) { OnLog?.Invoke("LM request cancelled."); return null; }
        catch (Exception ex)               { OnError?.Invoke($"LM failure: {ex.Message}"); return null; }
    }
}
