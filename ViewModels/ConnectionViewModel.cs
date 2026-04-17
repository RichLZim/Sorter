using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Sorter.Services;

namespace Sorter.ViewModels;

public class ConnectionViewModel : ViewModelBase
{
    private readonly LmStudioService _lmService;
    private readonly Action<string> _appendLog;

    private string _connectionState = "Disconnected";
    public string ConnectionState
    {
        get => _connectionState;
        set
        {
            if (RaiseAndSetIfChanged(ref _connectionState, value))
                UpdateConnectionColor();
        }
    }

    private string _connectionStatusText = "LMS SERVER: OFFLINE";
    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set => RaiseAndSetIfChanged(ref _connectionStatusText, value);
    }

    private IBrush _connectionColor = Brushes.Gray;
    public IBrush ConnectionColor
    {
        get => _connectionColor;
        set => RaiseAndSetIfChanged(ref _connectionColor, value);
    }

    // These are kept in sync by MainViewModel whenever the user edits them.
    public string LmStudioUrl { get; set; } = "";
    public string ModelName { get; set; } = "";

    public ICommand TestConnectionCommand { get; }

    /// <param name="lmService">Shared LM Studio service.</param>
    /// <param name="appendLog">Delegate from MainViewModel so test results appear in the activity log.</param>
    public ConnectionViewModel(LmStudioService lmService, Action<string> appendLog)
    {
        _lmService = lmService;
        _appendLog = appendLog;
        TestConnectionCommand = new RelayCommand(async () => await ExecuteTestConnectionAsync());
    }

    private async Task ExecuteTestConnectionAsync()
    {
        ConnectionStatusText = "LM STUDIO: TESTING...";
        ConnectionState = "Disconnected";

        // Always push the current URL/model into the service before testing.
        _lmService.UpdateSettings(LmStudioUrl, ModelName);

        var result = await _lmService.TestConnectionAsync();

        if (result.IsSuccess)
        {
            ConnectionState = "Connected";
            ConnectionStatusText = "LM STUDIO: CONNECTED";
            _appendLog($"[OK] LM Studio connected at {LmStudioUrl}");
        }
        else
        {
            ConnectionState = result.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                              result.Message.Contains("connection failed", StringComparison.OrdinalIgnoreCase)
                ? "Refused"
                : "Disconnected";

            ConnectionStatusText = $"LM STUDIO: {ConnectionState.ToUpper()}";
            _appendLog($"[ERROR] Connection failed: {result.Message}");
        }
    }

    private void UpdateConnectionColor()
    {
        ConnectionColor = ConnectionState switch
        {
            "Connected" => Brushes.Green,
            "Refused"   => Brushes.Red,
            _           => Brushes.Gray
        };
    }
}