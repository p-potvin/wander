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

        public FolderScanner(StateDatabase db, string syncRootPath)
        {
            _db = db;
            _syncRootPath = syncRootPath;
        }

        public async Task<ScanResult> ScanAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(_syncRootPath);

            var result = new ScanResult();
            var known = (await _db.GetAllStatesAsync()).ToDictionary(s => s.RelativePath, StringComparer.OrdinalIgnoreCase);
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
                    continue; // unchanged since last index; skip the expensive hash
                }

                var hash = HashHelper.ComputeFileHash(filePath);
                if (existing != null && !existing.IsDeleted && existing.Hash == hash)
                {
                    // Content identical, metadata drifted — refresh metadata only.
                    existing.SizeBytes = info.Length;
                    existing.LastModified = info.LastWriteTimeUtc;
                    await _db.UpsertFileStateAsync(existing);
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
    }
}
