using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Utils;

namespace Wander.Core.Services
{
    public class ScanResult
    {
        public int FilesSeen { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Tombstoned { get; set; }
        public int Merged { get; set; }
    }

    /// <summary>
    /// Walks the sync root and reconciles the on-disk truth into the state database.
    /// Mints a GUID the first time a path is seen; files that vanished since the last
    /// scan become tombstones so the delete propagates to peers.
    /// Runs at startup and as a periodic safety net behind the live watcher.
    /// </summary>
    public class FolderScanner
    {
        private readonly StateDatabase _db;
        private readonly string _syncRootPath;
        private readonly VersionRecorder? _versions;
        private readonly string _localNodeName;

        public FolderScanner(StateDatabase db, string syncRootPath,
            VersionRecorder? versions = null, string localNodeName = "local")
        {
            _db = db;
            _syncRootPath = syncRootPath;
            _versions = versions;
            _localNodeName = localNodeName;
        }

        public async Task<ScanResult> ScanAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(_syncRootPath);

            var result = new ScanResult();

            // Index by path. Two FileStates can share a path (the table is keyed by GUID, and
            // two peers can independently create the same path — open question #1).
            var known = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in (await _db.GetAllStatesAsync()).GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var live = group.Where(s => !s.IsDeleted).ToList();

                // Auto-merge the no-conflict case: several GUIDs claim one path with identical
                // content. Converge on the smallest GUID (deterministic across peers, so every
                // node reaches the same canonical id) and fold the losers' history into it.
                // Divergent content (a real conflict) is left for the future resolution screen.
                if (live.Count > 1 && live.Select(s => s.Hash).Distinct().Count() == 1)
                {
                    var canonical = live.OrderBy(s => s.Guid, StringComparer.Ordinal).First();
                    foreach (var dup in live.Where(s => s.Guid != canonical.Guid))
                    {
                        await _db.ReassignVersionsAsync(dup.Guid, canonical.Guid);
                        await _db.DeleteStateAsync(dup.Guid);
                        result.Merged++;
                    }
                    known[group.Key] = canonical;
                }
                else
                {
                    // Keep the live row over a tombstone, then the most recent.
                    known[group.Key] = group.Aggregate((a, b) => PreferOver(b, a) ? b : a);
                }
            }
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in Directory.EnumerateFiles(_syncRootPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = PathUtils.ToRelativePath(_syncRootPath, filePath);
                if (PathUtils.IsInternal(relativePath)) continue;

                result.FilesSeen++;
                seenPaths.Add(relativePath);

                var info = new FileInfo(filePath);
                if (known.TryGetValue(relativePath, out var existing) && !existing.IsDeleted
                    && existing.SizeBytes == info.Length
                    && existing.LastModified == info.LastWriteTimeUtc)
                {
                    // Unchanged since last index; skip the expensive re-hash but make sure a
                    // baseline version exists (matters for files migrated from a pre-history db).
                    await RecordBaselineAsync(existing.Guid, relativePath, filePath, existing.Hash, info);
                    continue;
                }

                var hash = HashHelper.ComputeFileHash(filePath);
                if (existing != null && !existing.IsDeleted && existing.Hash == hash)
                {
                    // Content identical, metadata drifted — refresh metadata only.
                    existing.SizeBytes = info.Length;
                    existing.LastModified = info.LastWriteTimeUtc;
                    await _db.UpsertFileStateAsync(existing);
                    await RecordBaselineAsync(existing.Guid, relativePath, filePath, hash, info);
                    continue;
                }

                var state = new FileState
                {
                    Guid = existing?.Guid ?? Guid.NewGuid().ToString(),
                    RelativePath = relativePath,
                    SizeBytes = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    Hash = hash,
                    IsDeleted = false
                };

                await _db.UpsertFileStateAsync(state);
                await RecordBaselineAsync(state.Guid, relativePath, filePath, hash, info);
                if (existing == null) result.Added++; else result.Updated++;
            }

            // Anything indexed but no longer on disk was deleted while we weren't looking.
            foreach (var (relativePath, state) in known)
            {
                if (state.IsDeleted || seenPaths.Contains(relativePath)) continue;

                await _db.MarkDeletedAsync(state.Guid, DateTime.UtcNow);
                result.Tombstoned++;
            }

            return result;
        }

        private Task RecordBaselineAsync(string guid, string relativePath, string filePath, string hash, FileInfo info)
        {
            // RecordAsync is a no-op when this content is already the newest recorded version,
            // so calling it on every scan is cheap (one indexed lookup) and self-deduping.
            return _versions?.RecordAsync(guid, relativePath, filePath, hash, info.Length,
                info.LastWriteTimeUtc, _localNodeName) ?? Task.CompletedTask;
        }

        /// <summary>Which of two same-path rows to treat as authoritative: live beats tombstone, then newer.</summary>
        private static bool PreferOver(FileState candidate, FileState current)
        {
            if (candidate.IsDeleted != current.IsDeleted) return !candidate.IsDeleted;
            return candidate.LastModified > current.LastModified;
        }
    }
}
