using System;
using System.Windows.Input;

namespace SimpleRollCall
{
    /// <summary>
    /// Minimal <see cref="ICommand"/> used by the title bar back button (ModernWpf's
    /// title bar only supports a command, not a click event).
    /// </summary>
    internal class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
