using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace ZKTECO_LIVE20R.Infrastructure;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }


    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        if( execute == null) throw new ArgumentNullException(nameof(execute));

        _execute = _ => execute();
        _canExecute = canExecute != null ? _ => canExecute() : null;

    }


    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }


    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute(parameter);
    }

    public void Execute(object? parameter)
    {
        _execute(parameter);
    }
}
