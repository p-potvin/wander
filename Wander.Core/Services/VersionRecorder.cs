using System;
using System.Linq;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Services.Retention;

namespace Wander.Core.Services
{
    /// <summary>
    /// Captures a version every time a file's content changes — from a local edit, the
    /// initial scan, or a pull from a peer. Stores the content once (content-addressed),
    /// appends a history row, then applies the retention policy (A.N.S.W.E.R.S.) to thin
    /// old versions and garbage-collects any blob no history row references any more.
    ///
    /// This is Phase 2's "Wander back in time" foundation: restoring is just copying a
    /// stored blob back to the working path, where it propagates as a normal edit.
    /// </summary>
    public class VersionRecorder
    {
        private readonly StateDatabase _db;
        private readonly VersionStore _store;
        private readonly IRetentionPolicy _retention;
        private readonly int _maxVersionsPerFile;

        public VersionRecorder(StateDatabase db, VersionStore store, IRetentionPolicy retention, int maxVersionsPerFile = 20)
        {
            _db = db;
            _store = store;
            _retention = retention;
            _maxVersionsPerFile = maxVersionsPerFile;
        }

        /// <summary>
        /// Record the current content of <paramref name="localFilePath"/> (whose hash is
        /// already known) as a new version of the file identified by <paramref name="guid"/>.
        /// Idempotent: recording the same content hash again is a no-op.
        /// </summary>
        public async Task RecordAsync(string guid, string relativePath, string localFilePath,
            string hash, long sizeBytes, DateTime modifiedUtc, string sourceNode)
        {
            if (string.IsNullOrEmpty(hash)) return;

            // Skip only if nothing changed since the last capture. A genuine revert (A→B→A)
            // still records a new version — the timeline should show the file changed back —
            // while the blob store dedupes the content, so it costs no extra disk.
            var existing = await _db.GetVersionsForGuidAsync(guid);
            if (existing.Count > 0 && existing[0].Hash == hash) return;

            await _store.StoreAsync(localFilePath, hash);
            await _db.AddVersionAsync(new FileVersion
            {
                Guid = guid,
                RelativePath = relativePath,
                Hash = hash,
                SizeBytes = sizeBytes,
                ModifiedUtc = modifiedUtc,
                SourceNode = sourceNode,
                RecordedUtc = DateTime.UtcNow
            });

            await ApplyRetentionAsync(guid);
        }

        private async Task ApplyRetentionAsync(string guid)
        {
            var versions = await _db.GetVersionsForGuidAsync(guid);
            var refs = versions.Select(v => new VersionRef(v.Id, v.RecordedUtc)).ToList();

            foreach (var id in _retention.SelectEvictions(guid, refs, _maxVersionsPerFile))
            {
                var evicted = versions.FirstOrDefault(v => v.Id == id);
                await _db.DeleteVersionAsync(id);
                if (evicted != null)
                {
                    // Only drop the blob if no surviving version (of any file) still points at it.
                    _store.DeleteBlobIfUnreferenced(evicted.Hash, await _db.IsHashReferencedAsync(evicted.Hash));
                }
            }
        }
    }
}
