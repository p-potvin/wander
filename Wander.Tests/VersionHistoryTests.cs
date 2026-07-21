using System.Linq;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Services;
using Wander.Core.Services.Retention;
using Wander.Core.Utils;
using Xunit;

namespace Wander.Tests
{
    public class VersionHistoryTests : IDisposable
    {
        private readonly TempDir _root = new();
        private readonly TempDir _dbDir = new();
        private readonly StateDatabase _db;
        private readonly VersionStore _store;
        private readonly VersionRecorder _recorder;

        public VersionHistoryTests()
        {
            _db = new StateDatabase(Path.Combine(_dbDir.Path, "state.db"));
            _db.InitializeAsync().GetAwaiter().GetResult();
            _store = new VersionStore(_root.Path);
            _recorder = new VersionRecorder(_db, _store, new AnswersRetention(), maxVersionsPerFile: 6);
        }

        public void Dispose()
        {
            _root.Dispose();
            _dbDir.Dispose();
        }

        private async Task<string> RecordEdit(string guid, string relativePath, string content, string source = "local")
        {
            var full = _root.WriteFile(relativePath, content);
            var hash = HashHelper.ComputeFileHash(full);
            var info = new FileInfo(full);
            await _recorder.RecordAsync(guid, relativePath, full, hash, info.Length, info.LastWriteTimeUtc, source);
            return hash;
        }

        [Fact]
        public async Task RecordsEachDistinctContentAsAVersion()
        {
            await RecordEdit("g1", "doc.txt", "v1");
            await RecordEdit("g1", "doc.txt", "v2");
            await RecordEdit("g1", "doc.txt", "v3");

            var versions = await _db.GetVersionsForGuidAsync("g1");
            Assert.Equal(3, versions.Count);
            Assert.Equal("g1", versions[0].Guid);
        }

        [Fact]
        public async Task IdenticalContentIsNotRecordedTwice()
        {
            await RecordEdit("g1", "doc.txt", "same");
            await RecordEdit("g1", "doc.txt", "same");

            Assert.Single(await _db.GetVersionsForGuidAsync("g1"));
        }

        [Fact]
        public async Task StoredVersionContentCanBeRestored()
        {
            var h1 = await RecordEdit("g1", "doc.txt", "original content");
            await RecordEdit("g1", "doc.txt", "changed content");

            var restorePath = Path.Combine(_root.Path, "restored.txt");
            await _store.RestoreToAsync(h1, restorePath);

            Assert.Equal("original content", File.ReadAllText(restorePath));
        }

        [Fact]
        public async Task DedupesBlobStorageAcrossVersions()
        {
            var hA = await RecordEdit("g1", "doc.txt", "AAA");
            await RecordEdit("g1", "doc.txt", "BBB");
            await RecordEdit("g1", "doc.txt", "AAA"); // content returns to an earlier value

            // "AAA" reappears as the newest, so it's re-recorded as a version...
            var versions = await _db.GetVersionsForGuidAsync("g1");
            Assert.Equal(3, versions.Count);
            // ...but the blob is content-addressed, so only one copy of "AAA" exists on disk.
            Assert.True(_store.Exists(hA));
        }

        [Fact]
        public async Task RetentionThinsToCapKeepingNewestAndOldest()
        {
            // 10 distinct edits, cap is 6.
            string? firstHash = null, lastHash = null;
            for (var i = 0; i < 10; i++)
            {
                var h = await RecordEdit("g1", "doc.txt", $"version {i} with distinct content here");
                firstHash ??= h;
                lastHash = h;
            }

            var versions = await _db.GetVersionsForGuidAsync("g1");
            Assert.Equal(6, versions.Count);
            Assert.Equal(lastHash, versions.First().Hash);       // newest kept
            Assert.Contains(versions, v => v.Hash == firstHash); // oldest kept
        }

        [Fact]
        public async Task EvictedBlobIsGarbageCollectedWhenUnreferenced()
        {
            var hashes = new System.Collections.Generic.List<string>();
            for (var i = 0; i < 10; i++)
            {
                hashes.Add(await RecordEdit("g1", "doc.txt", $"unique version {i} xyzzy"));
            }

            var survivors = (await _db.GetVersionsForGuidAsync("g1")).Select(v => v.Hash).ToHashSet();
            var evictedHash = hashes.First(h => !survivors.Contains(h));

            Assert.False(_store.Exists(evictedHash)); // blob removed for the thinned-out version
            Assert.True(_store.Exists(hashes.Last())); // newest blob still present
        }
    }
}
