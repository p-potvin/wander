using System.Linq;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Services;
using Xunit;

namespace Wander.Tests
{
    public class StateDatabaseTests : IDisposable
    {
        private readonly TempDir _dir = new();
        private readonly StateDatabase _db;

        public StateDatabaseTests()
        {
            _db = new StateDatabase(Path.Combine(_dir.Path, "state.db"));
        }

        public void Dispose() => _dir.Dispose();

        [Fact]
        public async Task UpsertThenGetRoundTrips()
        {
            await _db.InitializeAsync();
            var state = new FileState
            {
                Guid = Guid.NewGuid().ToString(),
                RelativePath = "docs/a.md",
                SizeBytes = 42,
                LastModified = DateTime.UtcNow,
                Hash = "abc",
                IsDeleted = false
            };

            await _db.UpsertFileStateAsync(state);

            var byGuid = await _db.GetFileStateByGuidAsync(state.Guid);
            var byPath = await _db.GetFileStateAsync("docs/a.md");
            Assert.Equal(state.Hash, byGuid!.Hash);
            Assert.Equal(state.Guid, byPath!.Guid);
        }

        [Fact]
        public async Task MarkDeletedCreatesTombstone()
        {
            await _db.InitializeAsync();
            var state = new FileState { Guid = "g1", RelativePath = "x.txt", Hash = "h", LastModified = DateTime.UtcNow.AddDays(-1) };
            await _db.UpsertFileStateAsync(state);

            var when = DateTime.UtcNow;
            await _db.MarkDeletedAsync("g1", when);

            var tombstone = await _db.GetFileStateByGuidAsync("g1");
            Assert.True(tombstone!.IsDeleted);
            Assert.True(tombstone.LastModified > state.LastModified);
        }
    }

    public class FolderScannerTests : IDisposable
    {
        private readonly TempDir _root = new();
        private readonly TempDir _dbDir = new();
        private readonly StateDatabase _db;
        private readonly FolderScanner _scanner;

        public FolderScannerTests()
        {
            _db = new StateDatabase(Path.Combine(_dbDir.Path, "state.db"));
            _scanner = new FolderScanner(_db, _root.Path);
        }

        public void Dispose()
        {
            _root.Dispose();
            _dbDir.Dispose();
        }

        [Fact]
        public async Task InitialScanMintsGuidsAndSecondScanIsStable()
        {
            await _db.InitializeAsync();
            _root.WriteFile("a.txt", "alpha");
            _root.WriteFile("sub/b.txt", "beta");

            var first = await _scanner.ScanAsync();
            Assert.Equal(2, first.Added);

            var guidsAfterFirst = (await _db.GetAllStatesAsync()).ToDictionary(s => s.RelativePath, s => s.Guid);

            var second = await _scanner.ScanAsync();
            Assert.Equal(0, second.Added);
            Assert.Equal(0, second.Updated);

            var guidsAfterSecond = (await _db.GetAllStatesAsync()).ToDictionary(s => s.RelativePath, s => s.Guid);
            Assert.Equal(guidsAfterFirst, guidsAfterSecond);
        }

        [Fact]
        public async Task ContentChangeKeepsGuid()
        {
            await _db.InitializeAsync();
            var path = _root.WriteFile("a.txt", "v1");
            await _scanner.ScanAsync();
            var original = await _db.GetFileStateAsync("a.txt");

            File.WriteAllText(path, "v2 with more content");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var result = await _scanner.ScanAsync();

            Assert.Equal(1, result.Updated);
            var updated = await _db.GetFileStateAsync("a.txt");
            Assert.Equal(original!.Guid, updated!.Guid);
            Assert.NotEqual(original.Hash, updated.Hash);
        }

        [Fact]
        public async Task MissingFileBecomesTombstone()
        {
            await _db.InitializeAsync();
            var path = _root.WriteFile("gone.txt", "bye");
            await _scanner.ScanAsync();

            File.Delete(path);
            var result = await _scanner.ScanAsync();

            Assert.Equal(1, result.Tombstoned);
            var state = await _db.GetFileStateAsync("gone.txt");
            Assert.True(state!.IsDeleted);
        }

        [Fact]
        public async Task ToleratesDuplicatePathsAcrossGuids()
        {
            // Two FileStates can share a path (independent GUIDs from two peers). The scan
            // must not crash building its path index — regression for the daemon startup crash.
            await _db.InitializeAsync();
            await _db.UpsertFileStateAsync(new FileState { Guid = "g-a", RelativePath = "dup.txt", Hash = "h1", LastModified = DateTime.UtcNow.AddMinutes(-5) });
            await _db.UpsertFileStateAsync(new FileState { Guid = "g-b", RelativePath = "dup.txt", Hash = "h2", LastModified = DateTime.UtcNow });
            _root.WriteFile("dup.txt", "content");

            var ex = await Record.ExceptionAsync(() => _scanner.ScanAsync());

            Assert.Null(ex); // no throw
        }

        [Fact]
        public async Task IgnoresWanderInternalDirectory()
        {
            await _db.InitializeAsync();
            _root.WriteFile(".wander/state.db", "not-synced");
            _root.WriteFile(".wander/trash/20260101T000000000Z/old.txt", "trashed");
            _root.WriteFile("real.txt", "synced");

            var result = await _scanner.ScanAsync();

            Assert.Equal(1, result.FilesSeen);
            Assert.Null(await _db.GetFileStateAsync(".wander/state.db"));
        }
    }

    public class TrashServiceTests : IDisposable
    {
        private readonly TempDir _root = new();

        public void Dispose() => _root.Dispose();

        [Fact]
        public void PreservesRelativePathInsideTrash()
        {
            var trash = new TrashService(_root.Path);
            var file = _root.WriteFile("docs/report.md", "important");

            var preserved = trash.MoveToTrash(file, DateTime.UtcNow);

            Assert.False(File.Exists(file));
            Assert.True(File.Exists(preserved));
            Assert.Contains(Path.Combine("docs", "report.md"), preserved);
            Assert.StartsWith(trash.TrashRootPath, preserved);
        }

        [Fact]
        public void PurgesOnlyExpiredBatches()
        {
            var trash = new TrashService(_root.Path, TimeSpan.FromDays(30));
            var oldFile = _root.WriteFile("old.txt", "x");
            var newFile = _root.WriteFile("new.txt", "y");

            var oldBatch = trash.MoveToTrash(oldFile, DateTime.UtcNow.AddDays(-45));
            var newBatch = trash.MoveToTrash(newFile, DateTime.UtcNow.AddDays(-5));

            var purged = trash.PurgeExpired(DateTime.UtcNow);

            Assert.Equal(1, purged);
            Assert.False(File.Exists(oldBatch));
            Assert.True(File.Exists(newBatch));
        }
    }
}
