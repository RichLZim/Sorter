using System;
using System.Threading;
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
    private readonly DispatcherTimer  _heartbeat;

    // Prevents concurrent heartbeat pings if a previous one is still in flight.
    private int _heartbeatRunning; // 0 = idle, 1 = running (Interlocked flag)
    private bool _disposed;

    public Action<string>? OnLogMessage { get; set; }

    [ObservableProperty] private string _connectionState      = "Disconnected";
    [ObservableProperty] private string _connectionStatusText = "LMS SERVER: OFFLINE";
    [ObservableProperty] private IBrush _connectionColor      = Brushes.Gray;
    [ObservableProperty] private string _serverButtonText     = "START SERVER";
    [ObservableProperty] private IBrush _serverButtonBackground =
        new SolidColorBrush(Color.Parse("#1A5C1A"));

    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#1A5C1A"));
    private static readonly IBrush OnlineBrush  = new SolidColorBrush(Color.Parse("#2EBD2E"));

    // Plain properties — set by MainViewModel to avoid circular dependencies
    public string LmStudioUrl { get; set; } = "";
    public string ModelName   { get; set; } = "";

    public ConnectionViewModel(LmStudioService lmService)
    {
        _lmService = lmService;
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeat.Tick += async (_, _) => await HeartbeatTickAsync();
        _heartbeat.Start();
    }

    partial void OnConnectionStateChanged(string value)
    {
        ConnectionColor = value switch
        {
            "Connected" => Brushes.Green,
            "Refused"   => Brushes.Red,
            _           => Brushes.Gray
        };

        (ServerButtonText, ServerButtonBackground) = value == "Connected"
            ? ("SERVER STARTED", OnlineBrush)
            : ("START SERVER",   OfflineBrush);
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        ConnectionStatusText = "LM STUDIO: TESTING...";
        ConnectionState      = "Disconnected";
        SyncSettings();
        var (ok, msg) = await _lmService.TestConnectionAsync();
        ApplyResult(ok, msg, logResult: true);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void SyncSettings() => _lmService.UpdateSettings(LmStudioUrl, ModelName);

    private async Task HeartbeatTickAsync()
    {
        // Skip if a previous ping is still in flight, or if we've been disposed.
        if (_disposed || Interlocked.CompareExchange(ref _heartbeatRunning, 1, 0) != 0)
            return;

        try
        {
            SyncSettings();
            var (ok, msg) = await _lmService.TestConnectionAsync();
            if (!_disposed)
                ApplyResult(ok, msg, logResult: false);
        }
        finally
        {
            Interlocked.Exchange(ref _heartbeatRunning, 0);
        }
    }

    private void ApplyResult(bool ok, string message, bool logResult)
    {
        if (ok)
        {
            ConnectionState      = "Connected";
            ConnectionStatusText = "LM STUDIO: CONNECTED";
            if (logResult) OnLogMessage?.Invoke($"[OK] Connected at {LmStudioUrl}");
        }
        else
        {
            ConnectionState = message.Contains("refused",          StringComparison.OrdinalIgnoreCase) ||
                              message.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
                ? "Refused" : "Disconnected";

            ConnectionStatusText = $"LM STUDIO: {ConnectionState.ToUpper()}";
            if (logResult) OnLogMessage?.Invoke($"[ERROR] Connection failed: {message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _heartbeat.Stop();
    }
}
