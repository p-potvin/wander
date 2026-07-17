using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Wander.Core.Models;
using Wander.Protocol;

namespace Wander.Core.Services
{
    public class PullSummary
    {
        public string PeerName { get; set; } = string.Empty;
        public int FilesConsidered { get; set; }
        public int Downloaded { get; set; }
        public int Conflicts { get; set; }
        public int Trashed { get; set; }
        public int Moved { get; set; }
        public int VerificationFailed { get; set; }
        public int Errors { get; set; }

        public override string ToString() =>
            $"pull from {PeerName}: {FilesConsidered} considered, {Downloaded} downloaded, {Moved} moved, " +
            $"{Conflicts} conflicts, {Trashed} trashed, {VerificationFailed} unverified, {Errors} errors";
    }

    /// <summary>
    /// One sync round against one peer: pull their manifest, let the SyncEngine decide
    /// per file, download what's needed. Wander is pull-only — every peer serves what it
    /// has and takes what it's missing, which is also what makes opportunistic relay work:
    /// whoever holds the newest version serves it, no special roles.
    /// </summary>
    public class SyncOrchestrator
    {
        public const int ProtocolVersion = 1;

        private readonly SyncEngine _engine;
        private readonly string _localNodeName;

        public SyncOrchestrator(SyncEngine engine, string localNodeName)
        {
            _engine = engine;
            _localNodeName = localNodeName;
        }

        public async Task<PullSummary> PullFromPeerAsync(
            SyncService.SyncServiceClient client,
            CancellationToken ct = default)
        {
            var ping = await client.PingAsync(
                new PingRequest { ProtocolVersion = ProtocolVersion, NodeName = _localNodeName },
                cancellationToken: ct);

            if (ping.ProtocolVersion != ProtocolVersion)
            {
                throw new InvalidOperationException(
                    $"Peer '{ping.NodeName}' speaks protocol v{ping.ProtocolVersion}, expected v{ProtocolVersion}.");
            }

            var summary = new PullSummary { PeerName = ping.NodeName };

            using var manifest = client.ListFiles(new ManifestRequest(), cancellationToken: ct);
            await foreach (var response in manifest.ResponseStream.ReadAllAsync(ct))
            {
                if (!response.Exists) continue;
                summary.FilesConsidered++;

                try
                {
                    var remote = response.ToFileState();
                    var action = await _engine.ProcessRemoteFileStateAsync(
                        remote,
                        () => client.DownloadFile(new DownloadRequest { Guid = remote.Guid }, cancellationToken: ct));

                    switch (action)
                    {
                        case SyncAction.Downloaded: summary.Downloaded++; break;
                        case SyncAction.DownloadedWithConflictCopy: summary.Downloaded++; summary.Conflicts++; break;
                        case SyncAction.Moved: summary.Moved++; break;
                        case SyncAction.Trashed: summary.Trashed++; break;
                        case SyncAction.SkippedFailedVerification: summary.VerificationFailed++; break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    summary.Errors++;
                    Console.WriteLine($"[Sync] Error applying '{response.RelativePath}' from {summary.PeerName}: {ex.Message}");
                }
            }

            return summary;
        }
    }
}
