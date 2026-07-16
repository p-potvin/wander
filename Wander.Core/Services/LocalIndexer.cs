using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Utils;

namespace Wander.Core.Services
{
    /// <summary>
    /// Consumes raw file-change notifications (from FolderWatcher or any other source),
    /// debounces the event storms FileSystemWatcher produces for a single save, then
    /// reconciles each settled path into the state database.
    /// Renames keep the file's GUID so peers treat them as moves, not delete+create.
    /// </summary>
    public class LocalIndexer : IDisposable
    {
        public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(500);

        private readonly StateDatabase _db;
        private readonly string _syncRootPath;
        private readonly TimeSpan _quietPeriod;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
            new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<FileState>? StateChanged;

        public LocalIndexer(StateDatabase db, string syncRootPath, TimeSpan? quietPeriod = null)
        {
            _db = db;
            _syncRootPath = syncRootPath;
            _quietPeriod = quietPeriod ?? DefaultQuietPeriod;
        }

        public void NotifyChanged(string fullPath) => ScheduleReconcile(fullPath);

        public void NotifyDeleted(string fullPath) => ScheduleReconcile(fullPath);

        public void NotifyRenamed(string oldFullPath, string newFullPath)
        {
            // Handle the move synchronously-ish (no debounce): the GUID transfer must
            // happen before a scan mistakes the new path for a brand-new file.
            _ = Task.Run(() => ReconcileRenameAsync(oldFullPath, newFullPath));
        }

        /// <summary>Waits for all debounced work to settle. Intended for tests and shutdown.</summary>
        public async Task FlushAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!_pending.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }

        private void ScheduleReconcile(string fullPath)
        {
            var relativePath = PathUtils.ToRelativePath(_syncRootPath, fullPath);
            if (PathUtils.IsInternal(relativePath)) return;

            var cts = new CancellationTokenSource();
            var previous = _pending.AddOrUpdate(relativePath, cts, (_, old) =>
            {
                old.Cancel();
                old.Dispose();
                return cts;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_quietPeriod, cts.Token);
                    _pending.TryRemove(relativePath, out _);
                    await ReconcilePathAsync(relativePath);
                }
                catch (OperationCanceledException)
                {
                    // superseded by a newer event for the same path
                }
                catch (Exception ex)
                {
                    _pending.TryRemove(relativePath, out _);
                    Console.WriteLine($"[Indexer] Failed to reconcile '{relativePath}': {ex.Message}");
                }
            });
        }

        private async Task ReconcilePathAsync(string relativePath)
        {
            var localPath = PathUtils.ToLocalPath(_syncRootPath, relativePath);
            var existing = await _db.GetFileStateAsync(relativePath);

            if (!File.Exists(localPath))
            {
                if (Directory.Exists(localPath)) return; // directories are implicit in Wander's model

                if (existing != null && !existing.IsDeleted)
                {
                    await _db.MarkDeletedAsync(existing.Guid, DateTime.UtcNow);
                    existing.IsDeleted = true;
                    existing.LastModified = DateTime.UtcNow;
                    StateChanged?.Invoke(this, existing);
                }
                return;
            }

            FileState state;
            try
            {
                var info = new FileInfo(localPath);
                var hash = HashHelper.ComputeFileHash(localPath);
                if (hash.Length == 0) return; // vanished or unreadable mid-reconcile; a later event will retry

                if (existing != null && !existing.IsDeleted && existing.Hash == hash
                    && existing.LastModified == info.LastWriteTimeUtc)
                {
                    return;
                }

                state = new FileState
                {
                    Guid = existing?.Guid ?? Guid.NewGuid().ToString(),
                    RelativePath = relativePath,
                    SizeBytes = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    Hash = hash,
                    IsDeleted = false
                };
            }
            catch (IOException)
            {
                // File still locked by the writing process; the next change event retries.
                return;
            }

            await _db.UpsertFileStateAsync(state);
            StateChanged?.Invoke(this, state);
        }

        private async Task ReconcileRenameAsync(string oldFullPath, string newFullPath)
        {
            try
            {
                var oldRelative = PathUtils.ToRelativePath(_syncRootPath, oldFullPath);
                var newRelative = PathUtils.ToRelativePath(_syncRootPath, newFullPath);

                if (PathUtils.IsInternal(newRelative))
                {
                    // Moved into .wander/ (e.g. trashed) — treat as a delete of the old path.
                    if (!PathUtils.IsInternal(oldRelative)) ScheduleReconcile(oldFullPath);
                    return;
                }

                var existing = PathUtils.IsInternal(oldRelative) ? null : await _db.GetFileStateAsync(oldRelative);
                if (existing != null && !existing.IsDeleted && File.Exists(newFullPath))
                {
                    var info = new FileInfo(newFullPath);
                    existing.RelativePath = newRelative;
                    existing.SizeBytes = info.Length;
                    existing.LastModified = DateTime.UtcNow;
                    await _db.UpsertFileStateAsync(existing);
                    StateChanged?.Invoke(this, existing);
                }
                else
                {
                    ScheduleReconcile(newFullPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Indexer] Failed to reconcile rename: {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var cts in _pending.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _pending.Clear();
        }
    }
}
