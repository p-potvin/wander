using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Services;
using Wander.Core.Utils;
using Wander.Network;

namespace Wander.Dashboard
{
    public partial class HistoryWindow : Window
    {
        public record FileRow(string Guid, string RelativePath);
        public record VersionRow(string When, string Detail, string Hash, string Guid, string RelativePath, bool IsCurrent, bool CanRestore);

        private readonly StateDatabase _db;
        private readonly VersionStore _store;
        private readonly WanderOptions _options;
        private WindowNotificationManager? _notifications;

        public HistoryWindow() : this(Program.Node.Services) { }

        public HistoryWindow(IServiceProvider services)
        {
            InitializeComponent();
            _db = services.GetRequiredService<StateDatabase>();
            _store = services.GetRequiredService<VersionStore>();
            _options = services.GetRequiredService<IOptions<WanderOptions>>().Value;

            Opened += (_, _) => _notifications = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 3
            };
            _ = LoadFilesAsync();
        }

        private async Task LoadFilesAsync()
        {
            var states = (await _db.GetAllStatesAsync())
                .Where(s => !s.IsDeleted)
                .GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(s => s.LastModified).First()) // collapse same-path dup GUIDs
                .OrderBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(s => new FileRow(s.Guid, s.RelativePath))
                .ToList();
            await Dispatcher.UIThread.InvokeAsync(() => FilesList.ItemsSource = states);
        }

        private async void File_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => await LoadVersionsForSelectedAsync();

        private async Task LoadVersionsForSelectedAsync()
        {
            if (FilesList.SelectedItem is not FileRow file)
            {
                VersionsList.ItemsSource = null;
                return;
            }

            var current = await _db.GetFileStateByGuidAsync(file.Guid);
            var versions = await _db.GetVersionsForGuidAsync(file.Guid);

            var rows = versions.Select(v =>
            {
                var isCurrent = current != null && v.Hash == current.Hash;
                var detail = $"{FormatBytes(v.SizeBytes)} · from {v.SourceNode}";
                // Restoring the version that's already current would be a no-op.
                return new VersionRow(
                    v.RecordedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    detail, v.Hash, v.Guid, v.RelativePath, isCurrent, CanRestore: !isCurrent);
            }).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VersionsList.ItemsSource = rows;
                HintText.IsVisible = rows.Count == 0;
                if (rows.Count == 0) HintText.Text = "No versions recorded yet for this file.";
            });
        }

        private async void Restore_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: VersionRow row }) return;

            try
            {
                var target = PathUtils.ToLocalPath(_options.SyncRoot, row.RelativePath);
                await _store.RestoreToAsync(row.Hash, target);
                // The local watcher will re-index the restored content as a new version and
                // propagate it to peers — restore is just an edit that happens to be an old value.
                _notifications?.Show(new Notification(
                    "Restored",
                    $"{row.RelativePath} rolled back to its {row.When} version. It will sync to your team.",
                    NotificationType.Success, TimeSpan.FromSeconds(6)));

                await LoadVersionsForSelectedAsync(); // refresh the "current" marker
            }
            catch (Exception ex)
            {
                _notifications?.Show(new Notification("Restore failed", ex.Message, NotificationType.Error));
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.#} {units[unit]}";
        }
    }
}
