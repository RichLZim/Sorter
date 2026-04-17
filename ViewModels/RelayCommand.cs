using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Sorter.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Func<Task>? _asyncExecute;
    private readonly Action? _syncExecute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _syncExecute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _asyncExecute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        if (_asyncExecute is not null)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try { await _asyncExecute(); }
            finally { _isExecuting = false; RaiseCanExecuteChanged(); }
        }
        else
        {
            _syncExecute?.Invoke();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
