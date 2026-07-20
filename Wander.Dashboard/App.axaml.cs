using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wander.Core.Services;

namespace Wander.Dashboard
{
    public class App : Application
    {
        private MainWindow? _mainWindow;
        private SyncController? _controller;
        private NativeMenuItem? _trayPauseItem;
        private NativeMenuItem? _trayUpdateItem;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            _controller = Program.Node.Services.GetRequiredService<SyncController>();
            _trayPauseItem = FindTrayItem("Pause", "Resume");
            _trayUpdateItem = FindTrayItem("update");

            _controller.PausedChanged += (_, paused) =>
                Dispatcher.UIThread.Post(() => ReflectTrayPause(paused));
            ReflectTrayPause(_controller.IsPaused);

            Program.Updates.UpdateReady += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (_trayUpdateItem != null) _trayUpdateItem.IsVisible = true;
                });

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

        // x:Name on a NativeMenuItem isn't supported, so match tray items by header text.
        private NativeMenuItem? FindTrayItem(params string[] headerContains)
        {
            var icons = TrayIcon.GetIcons(this);
            var menu = icons is { Count: > 0 } ? icons[0].Menu : null;
            if (menu == null) return null;
            return menu.Items
                .OfType<NativeMenuItem>()
                .FirstOrDefault(m => m.Header != null &&
                    headerContains.Any(h => m.Header.Contains(h, StringComparison.OrdinalIgnoreCase)));
        }

        private void ReflectTrayPause(bool paused)
        {
            if (_trayPauseItem != null)
            {
                _trayPauseItem.Header = paused ? "Resume syncing" : "Pause syncing";
            }
        }

        private void TrayOpen_Click(object? sender, EventArgs e)
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void TrayPause_Click(object? sender, EventArgs e)
        {
            _controller?.Toggle(); // ReflectTrayPause runs via PausedChanged
        }

        private void TrayUpdate_Click(object? sender, EventArgs e)
        {
            Program.Updates.ApplyAndRestart();
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
