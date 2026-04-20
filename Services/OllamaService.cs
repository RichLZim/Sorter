using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Sorter.Services;

/// <summary>
/// Talks to a locally-running Ollama instance.
/// POST /api/chat — images are passed in the "images" base64 array (Ollama native format).
/// </summary>
public class OllamaService
{
    private readonly HttpClient _client;
    private string _baseUrl = "http://127.0.0.1:11434";
    private string _model   = "gemma4:e4b";

    public event Action<string>? OnLog;
    public event Action<string>? OnError;

    public OllamaService(HttpClient client) => _client = client;

    public void UpdateSettings(string baseUrl, string model)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl.TrimEnd('/');
        _model   = string.IsNullOrWhiteSpace(model) ? _model : model;
    }

    public async Task<(bool IsSuccess, string Message)> TestConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await _client.GetAsync($"{_baseUrl}/api/tags", cts.Token);
            return resp.IsSuccessStatusCode ? (true, "OK") : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException) { return (false, "Connection timed out"); }
        catch (HttpRequestException ex)    { return (false, ex.Message); }
        catch (Exception ex)               { return (false, ex.Message); }
    }

    /// <summary>Returns currently loaded Ollama model names via /api/ps.</summary>
    public async Task<List<string>> GetRunningModelsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await _client.GetAsync($"{_baseUrl}/api/ps", cts.Token);
            if (!resp.IsSuccessStatusCode) return new List<string>();

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var arr))
                foreach (var m in arr.EnumerateArray())
                    if (m.TryGetProperty("name", out var n))
                        models.Add(n.GetString() ?? "");
            return models;
        }
        catch { return new List<string>(); }
    }

    /// <summary>Unloads all running Ollama models then starts the specified one.</summary>
    public async Task EnforceSingleModelAsync(string modelTag)
    {
        var running = await GetRunningModelsAsync();
        foreach (var m in running)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/delete")
                {
                    Content = new StringContent(
                        JsonConvert.SerializeObject(new { name = m }),
                        Encoding.UTF8, "application/json")
                };
                await _client.SendAsync(req);
            }
            catch { /* best-effort unload */ }
        }

        // Start the target model via CLI — FIX: pass exe/args as two separate params, not a tuple
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await RunAsync("cmd", $"/c ollama run {modelTag} --nowordwrap");
        else
            await RunAsync("/bin/sh", $"-l -c \"ollama run {modelTag} --nowordwrap &\"");
    }

    public static Task PullModelAsync(string modelTag)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RunAsync("cmd", $"/c ollama pull {modelTag}");
        else
            return RunAsync("/bin/sh", $"-l -c \"ollama pull {modelTag}\"");
    }

    public static Task StartServeAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RunAsync("cmd", "/c start /B ollama serve");
        else
            return RunAsync("/bin/sh", "-l -c \"ollama serve &\"");
    }

    public async Task<string?> QueryAsync(
        string  prompt,
        CancellationToken token,
        string? imageBase64 = null,
        int     maxTokens   = 0,
        double  temperature = 0.2,
        double  topP        = 0.9)
    {
        try
        {
            object message = imageBase64 is not null
                ? new { role = "user", content = prompt, images = new[] { imageBase64 } }
                : (object)new { role = "user", content = prompt };

            var options = maxTokens > 0
                ? (object)new Dictionary<string, object> { ["temperature"] = temperature, ["num_predict"] = maxTokens, ["top_p"] = topP }
                : new Dictionary<string, object>         { ["temperature"] = temperature, ["top_p"] = topP };

            var payload = new Dictionary<string, object>
            {
                ["model"]    = _model,
                ["messages"] = new[] { message },
                ["options"]  = options,
                ["stream"]   = false,
            };

            var json    = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"{_baseUrl}/api/chat", content, token);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(token);
                OnError?.Invoke($"Ollama HTTP {(int)response.StatusCode}: {err}");
                return null;
            }

            var result = await response.Content.ReadAsStringAsync(token);
            OnLog?.Invoke("Ollama response received.");
            return result;
        }
        catch (OperationCanceledException) { OnLog?.Invoke("Ollama request cancelled."); return null; }
        catch (Exception ex)               { OnError?.Invoke($"Ollama failure: {ex.Message}"); return null; }
    }

    private static async Task RunAsync(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                UseShellExecute = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is not null) await p.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            // Non-fatal — surface via OnError if needed
            Console.Error.WriteLine($"[Ollama CLI] {ex.Message}");
        }
    }
}
