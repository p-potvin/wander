using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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

        private readonly StateDatabase _db;
        private readonly TailscaleService _tailscale;
        private readonly ActivityLog _activity;
        private readonly WanderOptions _options;
        private readonly DispatcherTimer _refreshTimer;
        private bool _refreshing;

        // XAML previewer only; the real entry point injects the node's services.
        public MainWindow() : this(Program.Node.Services) { }

        public MainWindow(IServiceProvider services)
        {
            InitializeComponent();

            _db = services.GetRequiredService<StateDatabase>();
            _tailscale = services.GetRequiredService<TailscaleService>();
            _activity = services.GetRequiredService<ActivityLog>();
            _options = services.GetRequiredService<IOptions<WanderOptions>>().Value;

            NodeName.Text = _options.NodeName;
            SyncRootText.Text = _options.SyncRoot;
            var ip = WanderHost.GetTailscaleIpAddress();
            NodeAddress.Text = ip != null ? $"{ip}:{_options.Port} (tailnet)" : "no tailscale interface";

            _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
                (_, _) => _ = RefreshAsync());
            _refreshTimer.Start();
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                var states = (await _db.GetAllStatesAsync()).ToList();
                var peers = await _tailscale.GetOnlinePeersAsync();
                var feed = _activity.Snapshot()
                    .Select(e => new ActivityRow(e.AtUtc.ToLocalTime().ToString("HH:mm:ss"), e.Category, e.Message))
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var live = states.Count(s => !s.IsDeleted);
                    FileCount.Text = $"{live} files tracked · {FormatBytes(states.Where(s => !s.IsDeleted).Sum(s => s.SizeBytes))}";

                    PeersList.ItemsSource = peers;
                    NoPeersText.IsVisible = peers.Count == 0;

                    ActivityList.ItemsSource = feed;

                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#5BAD72"));
                    StatusText.Text = $"Watching · {peers.Count} peer(s) online";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusDot.Fill = new SolidColorBrush(Color.Parse("#E05C4A"));
                    StatusText.Text = $"Error: {ex.Message}";
                });
            }
            finally
            {
                _refreshing = false;
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
