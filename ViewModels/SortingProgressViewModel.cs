using System.Collections.ObjectModel;

namespace Sorter.ViewModels;

/// <summary>
/// Owns the runtime progress and log state that is produced during a sort run.
/// Nothing persisted here — no settings, no connection state.
/// </summary>
public class SortingProgressViewModel : ViewModelBase
{
    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private string _progressText = "0/0";
    public string ProgressText
    {
        get => _progressText;
        set => RaiseAndSetIfChanged(ref _progressText, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => RaiseAndSetIfChanged(ref _statusText, value);
    }

    private string _footerStatusText = "Configure folders above and press START SORT";
    public string FooterStatusText
    {
        get => _footerStatusText;
        set => RaiseAndSetIfChanged(ref _footerStatusText, value);
    }

    private string _resultCountText = "0 files";
    public string ResultCountText
    {
        get => _resultCountText;
        set => RaiseAndSetIfChanged(ref _resultCountText, value);
    }

    private int _inputTokens;
    public int InputTokens
    {
        get => _inputTokens;
        set => RaiseAndSetIfChanged(ref _inputTokens, value);
    }

    private int _outputTokens;
    public int OutputTokens
    {
        get => _outputTokens;
        set => RaiseAndSetIfChanged(ref _outputTokens, value);
    }

    private string _tokensPerSecond = "—";
    public string TokensPerSecond
    {
        get => _tokensPerSecond;
        set => RaiseAndSetIfChanged(ref _tokensPerSecond, value);
    }

    public ObservableCollection<string> LogEntries    { get; } = new();
    public ObservableCollection<string> ProcessedFiles { get; } = new();

    public void Reset()
    {
        ProgressValue   = 0;
        ProgressText    = "0/0";
        StatusText      = "Ready";
        InputTokens     = 0;
        OutputTokens    = 0;
        TokensPerSecond = "—";
        ResultCountText = "0 files";
        ProcessedFiles.Clear();
    }
}