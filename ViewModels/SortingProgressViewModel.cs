using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sorter.ViewModels;

public partial class SortingProgressViewModel : ObservableObject
{
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "0/0";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _footerStatusText = "Configure folders above and press START SORT";
    [ObservableProperty] private string _resultCountText = "0 files";
    [ObservableProperty] private int _inputTokens;
    [ObservableProperty] private int _outputTokens;
    [ObservableProperty] private string _tokensPerSecond = "—";

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<string> ProcessedFiles { get; } = new();

    public void Reset()
    {
        ProgressValue = 0;
        ProgressText = "0/0";
        StatusText = "Ready";
        InputTokens = 0;
        OutputTokens = 0;
        TokensPerSecond = "—";
        ResultCountText = "0 files";
        ProcessedFiles.Clear();
    }
}