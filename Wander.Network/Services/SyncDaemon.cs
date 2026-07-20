using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wander.Core.Data;
using Wander.Core.Services;
using Wander.Protocol;

namespace Wander.Network.Services
{
    /// <summary>
    /// The long-running heart of a Wander node: indexes the sync root (initial scan +
    /// live watcher), then periodically discovers tailnet peers and pulls from each one.
    /// </summary>
    public class SyncDaemon : BackgroundService
    {
        private readonly StateDatabase _db;
        private readonly FolderScanner _scanner;
        private readonly LocalIndexer _indexer;
        private readonly SyncOrchestrator _orchestrator;
        private readonly TrashService _trash;
        private readonly TailscaleService _tailscale;
        private readonly ActivityLog _activity;
        private readonly SyncController _controller;
        private readonly WanderOptions _options;
        private readonly ILogger<SyncDaemon> _logger;

        private FolderWatcher? _watcher;
        private int _quietRounds;

        public SyncDaemon(
            StateDatabase db,
            FolderScanner scanner,
            LocalIndexer indexer,
            SyncOrchestrator orchestrator,
            TrashService trash,
            TailscaleService tailscale,
            ActivityLog activity,
            SyncController controller,
            IOptions<WanderOptions> options,
            ILogger<SyncDaemon> logger)
        {
            _db = db;
            _scanner = scanner;
            _indexer = indexer;
            _orchestrator = orchestrator;
            _trash = trash;
            _tailscale = tailscale;
            _activity = activity;
            _controller = controller;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _db.InitializeAsync();

            var scan = await _scanner.ScanAsync(stoppingToken);
            _logger.LogInformation("Initial scan: {Seen} files, {Added} new, {Updated} updated, {Tombstoned} tombstoned",
                scan.FilesSeen, scan.Added, scan.Updated, scan.Tombstoned);
            _activity.Add("scan", $"Indexed {scan.FilesSeen} file{(scan.FilesSeen == 1 ? "" : "s")} ({scan.Added} new, {scan.Updated} updated, {scan.Tombstoned} deleted)");

            var purged = _trash.PurgeExpired(DateTime.UtcNow);
            if (purged > 0) _logger.LogInformation("Purged {Count} expired trash batches", purged);

            _indexer.StateChanged += (_, state) =>
                _activity.Add("local", state.IsDeleted ? $"Deleted: {state.RelativePath}" : $"Changed: {state.RelativePath}");

            _controller.PausedChanged += (_, paused) =>
                _activity.Add("pause", paused ? "Syncing paused — this node is silent until resumed" : "Syncing resumed");

            _watcher = new FolderWatcher(_options.SyncRoot);
            _watcher.FileCreated += (_, e) => _indexer.NotifyChanged(e.FullPath);
            _watcher.FileChanged += (_, e) => _indexer.NotifyChanged(e.FullPath);
            _watcher.FileDeleted += (_, e) => _indexer.NotifyDeleted(e.FullPath);
            _watcher.FileRenamed += (_, e) => _indexer.NotifyRenamed(e.OldFullPath, e.FullPath);
            _watcher.Start();
            _logger.LogInformation("Watching {Root}", _options.SyncRoot);

            var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PullIntervalSeconds));
            // Live watcher events are the fast path; this is the safety net in case one is
            // ever missed (locked files, buffer overflows) — without it, a file the watcher
            // dropped would never get indexed again until the process restarts.
            var rescanEvery = TimeSpan.FromMinutes(2);
            var nextRescan = DateTime.UtcNow + rescanEvery;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Paused: local indexing (watcher) keeps running so resume is instant,
                    // but we neither pull from peers nor advertise to them.
                    if (_controller.IsPaused)
                    {
                        _quietRounds = 0;
                    }
                    else
                    {
                        if (DateTime.UtcNow >= nextRescan)
                        {
                            nextRescan = DateTime.UtcNow + rescanEvery;
                            var rescan = await _scanner.ScanAsync(stoppingToken);
                            if (rescan.Added + rescan.Updated + rescan.Tombstoned > 0)
                            {
                                _activity.Add("scan", $"Re-scan caught {rescan.Added} new, {rescan.Updated} updated, {rescan.Tombstoned} deleted (missed by the live watcher)");
                            }
                        }

                        await RunSyncRoundAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Sync round failed");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunSyncRoundAsync(CancellationToken ct)
        {
            var addresses = new List<string>(_options.StaticPeers);

            foreach (var peer in await _tailscale.GetOnlinePeersAsync(ct))
            {
                addresses.Add($"http://{peer.Ip}:{_options.Port}");
            }

            var reached = new List<PullSummary>();
            var unreachable = 0;

            foreach (var address in addresses.Distinct())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new SyncService.SyncServiceClient(channel);
                    var summary = await _orchestrator.PullFromPeerAsync(client, ct);
                    reached.Add(summary);

                    if (summary.Downloaded + summary.Moved + summary.Conflicts + summary.Trashed + summary.VerificationFailed + summary.Errors > 0)
                    {
                        _logger.LogInformation("Sync {Summary}", summary);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Peers without Wander (or asleep) are normal on a tailnet; keep it quiet in the log...
                    unreachable++;
                    _logger.LogDebug("Peer {Address} unreachable: {Message}", address, ex.Message);
                }
            }

            // ...but always leave a trace in the activity feed, even a "checked, nothing changed"
            // round — otherwise a WinExe app with no console gives the user zero signal at all.
            if (reached.Count == 0 && unreachable == 0) return; // no peers configured/discovered yet

            var changed = reached.Sum(s => s.Downloaded + s.Moved + s.Conflicts + s.Trashed);
            var verificationFailed = reached.Sum(s => s.VerificationFailed);
            var quiet = changed == 0 && verificationFailed == 0;

            if (quiet)
            {
                _quietRounds++;
                // Show the first "nothing changed" round immediately for reassurance, then
                // only every 4th (~1/min at the default interval) so it doesn't drown real events.
                if (_quietRounds != 1 && _quietRounds % 4 != 0) return;
            }
            else
            {
                _quietRounds = 0;
            }

            var message = reached.Count == 0
                ? $"No peers reachable ({unreachable} tried)"
                : quiet
                    ? $"Checked {reached.Count} peer{(reached.Count == 1 ? "" : "s")} ({string.Join(", ", reached.Select(s => s.PeerName))}) — up to date"
                    : string.Join("; ", reached.Where(s => s.Downloaded + s.Moved + s.Conflicts + s.Trashed + s.VerificationFailed > 0)
                        .Select(s => $"{s.PeerName}: {s.Downloaded} downloaded, {s.Moved} moved, {s.Conflicts} conflicts, {s.Trashed} trashed" +
                            (s.VerificationFailed > 0 ? $", {s.VerificationFailed} unverified" : "")));

            _activity.Add("sync", message);
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            _indexer.Dispose();
            base.Dispose();
        }
    }
}
