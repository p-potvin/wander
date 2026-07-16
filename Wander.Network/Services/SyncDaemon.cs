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
        private readonly WanderOptions _options;
        private readonly ILogger<SyncDaemon> _logger;

        private FolderWatcher? _watcher;

        public SyncDaemon(
            StateDatabase db,
            FolderScanner scanner,
            LocalIndexer indexer,
            SyncOrchestrator orchestrator,
            TrashService trash,
            TailscaleService tailscale,
            ActivityLog activity,
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
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _db.InitializeAsync();

            var scan = await _scanner.ScanAsync(stoppingToken);
            _logger.LogInformation("Initial scan: {Seen} files, {Added} new, {Updated} updated, {Tombstoned} tombstoned",
                scan.FilesSeen, scan.Added, scan.Updated, scan.Tombstoned);
            _activity.Add("scan", $"Indexed {scan.FilesSeen} files ({scan.Added} new, {scan.Updated} updated, {scan.Tombstoned} deleted)");

            var purged = _trash.PurgeExpired(DateTime.UtcNow);
            if (purged > 0) _logger.LogInformation("Purged {Count} expired trash batches", purged);

            _indexer.StateChanged += (_, state) =>
                _activity.Add("local", state.IsDeleted ? $"Deleted: {state.RelativePath}" : $"Changed: {state.RelativePath}");

            _watcher = new FolderWatcher(_options.SyncRoot);
            _watcher.FileCreated += (_, e) => _indexer.NotifyChanged(e.FullPath);
            _watcher.FileChanged += (_, e) => _indexer.NotifyChanged(e.FullPath);
            _watcher.FileDeleted += (_, e) => _indexer.NotifyDeleted(e.FullPath);
            _watcher.FileRenamed += (_, e) => _indexer.NotifyRenamed(e.OldFullPath, e.FullPath);
            _watcher.Start();
            _logger.LogInformation("Watching {Root}", _options.SyncRoot);

            var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PullIntervalSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunSyncRoundAsync(stoppingToken);
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

            foreach (var address in addresses.Distinct())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var channel = GrpcChannel.ForAddress(address);
                    var client = new SyncService.SyncServiceClient(channel);
                    var summary = await _orchestrator.PullFromPeerAsync(client, ct);

                    if (summary.Downloaded + summary.Moved + summary.Conflicts + summary.Trashed + summary.Errors > 0)
                    {
                        _logger.LogInformation("Sync {Summary}", summary);
                        _activity.Add("sync", $"From {summary.PeerName}: {summary.Downloaded} downloaded, {summary.Moved} moved, {summary.Conflicts} conflicts, {summary.Trashed} trashed" +
                            (summary.Errors > 0 ? $", {summary.Errors} errors" : ""));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Peers without Wander (or asleep) are normal on a tailnet; keep it quiet.
                    _logger.LogDebug("Peer {Address} unreachable: {Message}", address, ex.Message);
                }
            }
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            _indexer.Dispose();
            base.Dispose();
        }
    }
}
