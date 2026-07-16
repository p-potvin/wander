using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Utils;
using Wander.Protocol;

namespace Wander.Core.Services
{
    public enum SyncAction
    {
        None,
        Downloaded,
        DownloadedWithConflictCopy,
        Moved,
        Trashed,
        SkippedLocalNewer,
        SkippedFailedVerification
    }

    /// <summary>
    /// Applies a remote peer's file state to the local disk under Wander's lenient policy:
    /// the newest edit wins everywhere, and a losing local edit is never destroyed — it is
    /// preserved beside the file as an attributed conflict copy. Remote deletes go to
    /// .wander/trash, never straight to oblivion.
    /// </summary>
    public class SyncEngine
    {
        private readonly StateDatabase _db;
        private readonly string _syncRootPath;
        private readonly TrashService _trash;
        private readonly string _localNodeName;
        private readonly ActivityLog? _activity;

        public SyncEngine(StateDatabase db, string syncRootPath, TrashService trash, string localNodeName,
            ActivityLog? activity = null)
        {
            _db = db;
            _syncRootPath = syncRootPath;
            _trash = trash;
            _localNodeName = localNodeName;
            _activity = activity;
        }

        public async Task<SyncAction> ProcessRemoteFileStateAsync(
            FileState remote,
            Func<AsyncServerStreamingCall<FileChunk>>? openDownload)
        {
            var localPath = PathUtils.ToLocalPath(_syncRootPath, remote.RelativePath);
            var localState = await _db.GetFileStateByGuidAsync(remote.Guid);

            if (remote.IsDeleted)
            {
                return await ApplyRemoteDeleteAsync(remote, localPath, localState);
            }

            // Rename propagation: same GUID, same content, different path — move, don't re-download.
            if (localState != null && !localState.IsDeleted
                && !localState.RelativePath.Equals(remote.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = PathUtils.ToLocalPath(_syncRootPath, localState.RelativePath);
                if (File.Exists(oldPath) && !File.Exists(localPath)
                    && HashHelper.ComputeFileHash(oldPath) == remote.Hash
                    && remote.LastModified >= localState.LastModified)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    File.Move(oldPath, localPath);
                    await _db.UpsertFileStateAsync(remote);
                    Console.WriteLine($"[Sync] Moved: {localState.RelativePath} -> {remote.RelativePath}");
                    _activity?.Add("move", $"{localState.RelativePath} → {remote.RelativePath}");
                    return SyncAction.Moved;
                }
            }

            if (!File.Exists(localPath))
            {
                // Our own newer tombstone wins over the remote copy; otherwise fetch it.
                if (localState is { IsDeleted: true } && localState.LastModified > remote.LastModified)
                {
                    return SyncAction.SkippedLocalNewer;
                }

                return await DownloadAsync(remote, localPath, openDownload, conflictCopy: false);
            }

            var localHash = HashHelper.ComputeFileHash(localPath);
            if (localHash == remote.Hash)
            {
                await _db.UpsertFileStateAsync(remote); // content already identical; adopt metadata
                return SyncAction.None;
            }

            var localMtimeUtc = File.GetLastWriteTimeUtc(localPath);
            var localIsDirty = localState == null || localState.IsDeleted || localHash != localState.Hash;

            if (!localIsDirty)
            {
                // Local matches its last-indexed state: a plain fast-forward, no conflict.
                return remote.LastModified >= localState!.LastModified
                    ? await DownloadAsync(remote, localPath, openDownload, conflictCopy: false)
                    : SyncAction.SkippedLocalNewer;
            }

            // Both sides changed since the last common state: a real conflict.
            if (remote.LastModified > localMtimeUtc)
            {
                // Remote wins; the local edit is preserved and attributed to this node.
                var conflictPath = ConflictNaming.BuildConflictPath(localPath, _localNodeName, DateTime.UtcNow);
                File.Move(localPath, conflictPath);
                Console.WriteLine($"[Conflict] Local edit preserved as: {Path.GetFileName(conflictPath)}");
                _activity?.Add("conflict", $"{remote.RelativePath}: local edit preserved as {Path.GetFileName(conflictPath)}");
                return await DownloadAsync(remote, localPath, openDownload, conflictCopy: true);
            }

            // Local edit is newer: we win. The peer will make its own conflict copy when it pulls from us.
            return SyncAction.SkippedLocalNewer;
        }

        private async Task<SyncAction> ApplyRemoteDeleteAsync(FileState remote, string localPath, FileState? localState)
        {
            if (!File.Exists(localPath))
            {
                if (localState == null || !localState.IsDeleted)
                {
                    await _db.UpsertFileStateAsync(remote); // adopt the tombstone
                }
                return SyncAction.None;
            }

            var localHash = HashHelper.ComputeFileHash(localPath);
            var localIsDirty = localState == null || localHash != localState.Hash;

            if (localIsDirty && File.GetLastWriteTimeUtc(localPath) > remote.LastModified)
            {
                // Local edit is newer than the remote delete — the edit wins and will resurrect the file.
                return SyncAction.SkippedLocalNewer;
            }

            var trashedTo = _trash.MoveToTrash(localPath, DateTime.UtcNow);
            await _db.UpsertFileStateAsync(remote);
            Console.WriteLine($"[Sync] Remote delete applied; preserved in trash: {trashedTo}");
            _activity?.Add("trash", $"{remote.RelativePath}: deleted by peer, preserved in trash (30 days)");
            return SyncAction.Trashed;
        }

        private async Task<SyncAction> DownloadAsync(
            FileState remote,
            string localPath,
            Func<AsyncServerStreamingCall<FileChunk>>? openDownload,
            bool conflictCopy)
        {
            if (openDownload == null) return SyncAction.None;

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            // Stream to a temp file first so a dropped connection never leaves a torn file in place.
            var tempPath = localPath + ".wander-tmp";
            using (var call = openDownload())
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await foreach (var chunk in call.ResponseStream.ReadAllAsync())
                {
                    await fs.WriteAsync(chunk.Data.Memory);
                    if (chunk.IsFinal) break;
                }
            }

            // Guardrail: verify what we received is what the manifest promised.
            var receivedHash = HashHelper.ComputeFileHash(tempPath);
            if (receivedHash != remote.Hash)
            {
                File.Delete(tempPath);
                Console.WriteLine($"[Sync] Discarded '{remote.RelativePath}': hash mismatch after download (peer file changed mid-transfer?). Will retry next round.");
                return SyncAction.SkippedFailedVerification;
            }

            File.Move(tempPath, localPath, overwrite: true);
            File.SetLastWriteTimeUtc(localPath, remote.LastModified);

            await _db.UpsertFileStateAsync(new FileState
            {
                Guid = remote.Guid,
                RelativePath = remote.RelativePath,
                Hash = remote.Hash,
                SizeBytes = remote.SizeBytes,
                LastModified = remote.LastModified,
                IsDeleted = false
            });

            Console.WriteLine($"[Sync] Downloaded: {remote.RelativePath}");
            if (!conflictCopy) _activity?.Add("pull", $"{remote.RelativePath} ({remote.SizeBytes:N0} B)");
            return conflictCopy ? SyncAction.DownloadedWithConflictCopy : SyncAction.Downloaded;
        }
    }
}
