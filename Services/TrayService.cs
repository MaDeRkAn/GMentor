using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;

namespace GMentor.Services
{
    public sealed class TrayService : IDisposable
    {
        private readonly TaskbarIcon _tray;
        private readonly Action _onOpen;
        private readonly Action _onHelp;
        private readonly Action _onQuit;

        public TrayService(Action onOpen, Action onHelp, Action onQuit)
        {
            _onOpen = onOpen;
            _onHelp = onHelp;
            _onQuit = onQuit;

            // TaskbarIcon resource must be declared in App.xaml as "AppTray"
            _tray = (TaskbarIcon)Application.Current.Resources["AppTray"];

            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem
            {
                Header = "Open",
                Command = new RelayCommand(_ => _onOpen())
            });

            menu.Items.Add(new MenuItem
            {
                Header = "How to Use",
                Command = new RelayCommand(_ => _onHelp())
            });

            menu.Items.Add(new Separator());

            menu.Items.Add(new MenuItem
            {
                Header = "Quit",
                Command = new RelayCommand(_ => _onQuit())
            });

            _tray.ContextMenu = menu;
            _tray.TrayMouseDoubleClick += (_, __) => _onOpen();
        }

        public void ShowBalloon(string message, string title = "GMentor", int timeoutMs = 4000)
        {
            _tray.ShowBalloonTip(title, message, BalloonIcon.Info);
        }

        public void Dispose()
        {
            _tray.Visibility = Visibility.Collapsed;
            _tray.Dispose();
        }
    }

    // Simple ICommand helper used by tray menu items
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        // Hook into WPF's command requery so the event is actually used and CS0067 goes away
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
