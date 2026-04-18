using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sorter.Services;

namespace Sorter.ViewModels;

public partial class ConnectionViewModel : ObservableObject, IDisposable
{
    private readonly LmStudioService _lmService;
    private readonly DispatcherTimer _heartbeat;
    public Action<string>? OnLogMessage { get; set; }

    [ObservableProperty] private string _connectionState = "Disconnected";
    [ObservableProperty] private string _connectionStatusText = "LMS SERVER: OFFLINE";
    [ObservableProperty] private IBrush _connectionColor = Brushes.Gray;
    [ObservableProperty] private string _serverButtonText = "START SERVER";
    [ObservableProperty] private IBrush _serverButtonBackground = new SolidColorBrush(Color.Parse("#1A5C1A"));

    private static readonly IBrush ServerOfflineBrush = new SolidColorBrush(Color.Parse("#1A5C1A"));
    private static readonly IBrush ServerOnlineBrush = new SolidColorBrush(Color.Parse("#2EBD2E"));

    public string LmStudioUrl { get; set; } = "";
    public string ModelName { get; set; } = "";

    public ConnectionViewModel(LmStudioService lmService)
    {
        _lmService = lmService;
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeat.Tick += async (_, _) => await HeartbeatTickAsync();
        _heartbeat.Start();
    }

    // Auto-triggered by CommunityToolkit when ConnectionState changes
    partial void OnConnectionStateChanged(string value)
    {
        ConnectionColor = value switch {
            "Connected" => Brushes.Green,
            "Refused" => Brushes.Red,
            _ => Brushes.Gray
        };
        
        if (value == "Connected") {
            ServerButtonText = "SERVER STARTED";
            ServerButtonBackground = ServerOnlineBrush;
        } else {
            ServerButtonText = "START SERVER";
            ServerButtonBackground = ServerOfflineBrush;
        }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        ConnectionStatusText = "LM STUDIO: TESTING...";
        ConnectionState = "Disconnected";
        _lmService.UpdateSettings(LmStudioUrl, ModelName);
        var result = await _lmService.TestConnectionAsync();
        ApplyConnectionResult(result.IsSuccess, result.Message, logResult: true);
    }

    private async Task HeartbeatTickAsync()
    {
        _lmService.UpdateSettings(LmStudioUrl, ModelName);
        var result = await _lmService.TestConnectionAsync();
        ApplyConnectionResult(result.IsSuccess, result.Message, logResult: false);
    }

    private void ApplyConnectionResult(bool isSuccess, string message, bool logResult)
    {
        if (isSuccess) {
            ConnectionState = "Connected";
            ConnectionStatusText = "LM STUDIO: CONNECTED";
            if (logResult) OnLogMessage?.Invoke($"[OK] LM Studio connected at {LmStudioUrl}");
        } else {
            ConnectionState = message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                              message.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
                ? "Refused" : "Disconnected";
            ConnectionStatusText = $"LM STUDIO: {ConnectionState.ToUpper()}";
            if (logResult) OnLogMessage?.Invoke($"[ERROR] Connection failed: {message}");
        }
    }

    public void Dispose() => _heartbeat.Stop();
}