using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wander.Core.Data;
using Wander.Core.Services;
using Wander.Network;
using Wander.Network.Services;

namespace Wander.Dashboard
{
    public partial class MainWindow : Window
    {
        public record ActivityRow(string Time, string Category, string Message);

        private static readonly IBrush SignalOnline = new SolidColorBrush(Color.Parse("#6BE675"));
        private static readonly IBrush SignalWarning = new SolidColorBrush(Color.Parse("#F0B94B"));
        private static readonly IBrush SignalAlert = new SolidColorBrush(Color.Parse("#FF6B7A"));

        private readonly StateDatabase _db;
        private readonly TailscaleService _tailscale;
        private readonly ActivityLog _activity;
        private readonly SyncController _controller;
        private readonly WanderOptions _options;
        private readonly DispatcherTimer _refreshTimer;
        private WindowNotificationManager? _notifications;
        private bool _refreshing;

        // XAML previewer only; the real entry point injects the node's services.
        public MainWindow() : this(Program.Node.Services) { }

        public MainWindow(IServiceProvider services)
        {
            InitializeComponent();

            _db = services.GetRequiredService<StateDatabase>();
            _tailscale = services.GetRequiredService<TailscaleService>();
            _activity = services.GetRequiredService<ActivityLog>();
            _controller = services.GetRequiredService<SyncController>();
            _options = services.GetRequiredService<IOptions<WanderOptions>>().Value;

            NodeName.Text = _options.NodeName;
            SyncRootText.Text = _options.SyncRoot;
            var ip = WanderHost.GetTailscaleIpAddress();
            NodeAddress.Text = ip != null ? $"{ip}:{_options.Port} (tailnet)" : "no tailscale interface";

            ReflectPauseState(_controller.IsPaused);
            _controller.PausedChanged += (_, paused) =>
                Dispatcher.UIThread.Post(() => ReflectPauseState(paused));

            // Important events (conflicts, deletes, failures) surface as toasts so the
            // user doesn't have to be watching the activity feed to catch them.
            _activity.EntryAdded += OnActivityEntryAdded;

            Opened += (_, _) => _notifications = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 4
            };

            _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
                (_, _) => _ = RefreshAsync());
            _refreshTimer.Start();
            _ = RefreshAsync();
        }

        private void Pause_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _controller.Toggle(); // ReflectPauseState runs via PausedChanged
        }

        private void ReflectPauseState(bool paused)
        {
            PauseButtonText.Text = paused ? "Resume" : "Pause";
            if (paused)
            {
                StatusDot.Fill = SignalWarning;
                StatusText.Text = "Paused";
            }
            // When resuming, the next RefreshAsync repaints the live status.
        }

        private void OnActivityEntryAdded(object? sender, ActivityEntry entry)
        {
            var (title, type) = entry.Category switch
            {
                "conflict" => ("Sync conflict", NotificationType.Warning),
                "trash" => ("File removed by a peer", NotificationType.Warning),
                "error" => ("Sync problem", NotificationType.Error),
                _ => (null, NotificationType.Information)
            };
            if (title == null) return;

            Dispatcher.UIThread.Post(() =>
                _notifications?.Show(new Notification(title, entry.Message, type, TimeSpan.FromSeconds(8))));
        }

        private async Task RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                var states = (await _db.GetAllStatesAsync()).ToList();
                var peers = await _tailscale.GetOnlinePeersAsync();
                var conflicts = FindConflictCopies();
                var feed = _activity.Snapshot()
                    .Select(e => new ActivityRow(e.AtUtc.ToLocalTime().ToString("HH:mm:ss"), e.Category, e.Message))
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var live = states.Count(s => !s.IsDeleted);
                    var bytes = FormatBytes(states.Where(s => !s.IsDeleted).Sum(s => s.SizeBytes));
                    FileCount.Text = live == 1 ? $"1 file tracked · {bytes}" : $"{live} files tracked · {bytes}";

                    PeersList.ItemsSource = peers;
                    NoPeersText.IsVisible = peers.Count == 0;

                    ConflictsList.ItemsSource = conflicts;
                    NoConflictsText.IsVisible = conflicts.Count == 0;

                    ActivityList.ItemsSource = feed;

                    // Paused state owns the status line while active; don't overwrite it.
                    if (!_controller.IsPaused)
                    {
                        StatusDot.Fill = SignalOnline;
                        StatusText.Text = peers.Count == 1 ? "Watching · 1 peer online" : $"Watching · {peers.Count} peers online";
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_controller.IsPaused) return;
                    StatusDot.Fill = SignalAlert;
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>Unresolved conflict copies on disk, named by ConflictNaming ("name (conflict — node, date).ext").</summary>
        private List<string> FindConflictCopies()
        {
            try
            {
                if (!Directory.Exists(_options.SyncRoot)) return [];
                return Directory.EnumerateFiles(_options.SyncRoot, "*(conflict — *", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.wander{Path.DirectorySeparatorChar}"))
                    .Select(f => Path.GetRelativePath(_options.SyncRoot, f))
                    .OrderBy(f => f)
                    .Take(20)
                    .ToList();
            }
            catch
            {
                return [];
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }
    }
}
