using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Wander.Dashboard
{
    public class App : Application
    {
        private MainWindow? _mainWindow;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // The engine outlives the window: closing the dashboard hides it to the
                // tray while sync keeps running. Quit is explicit, via the tray menu.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _mainWindow = new MainWindow(Program.Node.Services);
                _mainWindow.Closing += (_, e) =>
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                };
                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void TrayOpen_Click(object? sender, EventArgs e)
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void TrayQuit_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
