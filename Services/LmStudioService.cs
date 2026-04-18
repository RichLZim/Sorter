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

    /// <summary>Syncs URL/model from settings without requiring DI re-registration.</summary>
    public void UpdateSettings(string baseUrl, string model)
    {
        _baseUrl = baseUrl;
        _model   = model;
    }

    /// <summary>Pings the /v1/models endpoint to verify the server is reachable.</summary>
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
        catch (OperationCanceledException)
        {
            return (false, "Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string?> QueryAsync(string prompt, CancellationToken token)
    {
        try
        {
            var payload = new { model = _model, prompt, stream = false };
            var json    = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync($"{_baseUrl}/v1/completions", content, token);

            if (!response.IsSuccessStatusCode)
            {
                OnError?.Invoke($"LM Studio HTTP error: {response.StatusCode}");
                return null;
            }

            var result = await response.Content.ReadAsStringAsync(token);
            OnLog?.Invoke("LM Studio response received.");
            return result;
        }
        catch (OperationCanceledException)
        {
            OnLog?.Invoke("LM request cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"LM failure: {ex.Message}");
            return null;
        }
    }
}
