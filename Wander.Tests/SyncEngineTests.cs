using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Services;
using Wander.Core.Utils;
using Xunit;

namespace Wander.Tests
{
    /// <summary>The conflict-policy matrix: newest wins, losers are preserved, deletes go to trash.</summary>
    public class SyncEngineTests : IDisposable
    {
        private readonly TempDir _root = new();
        private readonly TempDir _dbDir = new();
        private readonly StateDatabase _db;
        private readonly SyncEngine _engine;
        private readonly FolderScanner _scanner;

        public SyncEngineTests()
        {
            _db = new StateDatabase(Path.Combine(_dbDir.Path, "state.db"));
            _db.InitializeAsync().GetAwaiter().GetResult();
            var trash = new TrashService(_root.Path);
            _engine = new SyncEngine(_db, _root.Path, trash, "TestNode");
            _scanner = new FolderScanner(_db, _root.Path);
        }

        public void Dispose()
        {
            _root.Dispose();
            _dbDir.Dispose();
        }

        private static FileState RemoteState(string relativePath, string content, DateTime modifiedUtc,
            string? guid = null, bool isDeleted = false)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new FileState
            {
                Guid = guid ?? Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SizeBytes = bytes.Length,
                LastModified = modifiedUtc,
                Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                IsDeleted = isDeleted
            };
        }

        [Fact]
        public async Task NewRemoteFileIsDownloadedAndVerified()
        {
            var remote = RemoteState("hello.txt", "hello from peer", DateTime.UtcNow);

            var action = await _engine.ProcessRemoteFileStateAsync(remote,
                FakeGrpc.DownloadOf(Encoding.UTF8.GetBytes("hello from peer")));

            Assert.Equal(SyncAction.Downloaded, action);
            Assert.Equal("hello from peer", File.ReadAllText(Path.Combine(_root.Path, "hello.txt")));
            Assert.Equal(remote.Hash, (await _db.GetFileStateByGuidAsync(remote.Guid))!.Hash);
        }

        [Fact]
        public async Task CorruptDownloadIsDiscarded()
        {
            var remote = RemoteState("hello.txt", "expected content", DateTime.UtcNow);

            var action = await _engine.ProcessRemoteFileStateAsync(remote,
                FakeGrpc.DownloadOf(Encoding.UTF8.GetBytes("SOMETHING ELSE")));

            Assert.Equal(SyncAction.SkippedFailedVerification, action);
            Assert.False(File.Exists(Path.Combine(_root.Path, "hello.txt")));
        }

        [Fact]
        public async Task CleanFastForwardDoesNotCreateConflictCopy()
        {
            // Local file is exactly what we last indexed; remote is newer → plain update.
            var path = _root.WriteFile("doc.txt", "version 1");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("doc.txt");

            var remote = RemoteState("doc.txt", "version 2", DateTime.UtcNow.AddMinutes(5), guid: local!.Guid);
            var action = await _engine.ProcessRemoteFileStateAsync(remote,
                FakeGrpc.DownloadOf(Encoding.UTF8.GetBytes("version 2")));

            Assert.Equal(SyncAction.Downloaded, action);
            Assert.Equal("version 2", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_root.Path, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(PathUtils.WanderDirName)));
        }

        [Fact]
        public async Task RealConflictPreservesLocalEditAsAttributedCopy()
        {
            var path = _root.WriteFile("doc.txt", "common ancestor");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("doc.txt");

            // Both sides edited since; the remote edit is newer.
            File.WriteAllText(path, "my local edit");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            var remote = RemoteState("doc.txt", "their remote edit", DateTime.UtcNow, guid: local!.Guid);

            var action = await _engine.ProcessRemoteFileStateAsync(remote,
                FakeGrpc.DownloadOf(Encoding.UTF8.GetBytes("their remote edit")));

            Assert.Equal(SyncAction.DownloadedWithConflictCopy, action);
            Assert.Equal("their remote edit", File.ReadAllText(path));

            var conflictCopy = Directory.GetFiles(_root.Path).Single(f => f.Contains("conflict"));
            Assert.Equal("my local edit", File.ReadAllText(conflictCopy));
            Assert.Contains("TestNode", conflictCopy);
        }

        [Fact]
        public async Task NewerLocalEditWinsAndIsKept()
        {
            var path = _root.WriteFile("doc.txt", "common ancestor");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("doc.txt");

            File.WriteAllText(path, "my newer local edit");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            var remote = RemoteState("doc.txt", "their older edit", DateTime.UtcNow.AddMinutes(-10), guid: local!.Guid);

            var action = await _engine.ProcessRemoteFileStateAsync(remote,
                FakeGrpc.DownloadOf(Encoding.UTF8.GetBytes("their older edit")));

            Assert.Equal(SyncAction.SkippedLocalNewer, action);
            Assert.Equal("my newer local edit", File.ReadAllText(path));
        }

        [Fact]
        public async Task RemoteDeleteMovesCleanLocalFileToTrash()
        {
            var path = _root.WriteFile("doomed.txt", "contents");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("doomed.txt");

            var tombstone = new FileState
            {
                Guid = local!.Guid,
                RelativePath = "doomed.txt",
                Hash = local.Hash,
                SizeBytes = local.SizeBytes,
                LastModified = DateTime.UtcNow.AddMinutes(1),
                IsDeleted = true
            };

            var action = await _engine.ProcessRemoteFileStateAsync(tombstone, null);

            Assert.Equal(SyncAction.Trashed, action);
            Assert.False(File.Exists(path));
            var trashed = Directory.GetFiles(Path.Combine(_root.Path, ".wander", "trash"), "*", SearchOption.AllDirectories);
            Assert.Single(trashed);
            Assert.Equal("contents", File.ReadAllText(trashed[0]));
            Assert.True((await _db.GetFileStateByGuidAsync(local.Guid))!.IsDeleted);
        }

        [Fact]
        public async Task NewerLocalEditSurvivesRemoteDelete()
        {
            var path = _root.WriteFile("survivor.txt", "ancestor");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("survivor.txt");

            File.WriteAllText(path, "edited after their delete");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            var tombstone = new FileState
            {
                Guid = local!.Guid,
                RelativePath = "survivor.txt",
                Hash = local.Hash,
                LastModified = DateTime.UtcNow.AddMinutes(-5),
                IsDeleted = true
            };

            var action = await _engine.ProcessRemoteFileStateAsync(tombstone, null);

            Assert.Equal(SyncAction.SkippedLocalNewer, action);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task RenamePropagatesAsMoveWithoutRedownload()
        {
            var oldPath = _root.WriteFile("old-name.txt", "same content");
            await _scanner.ScanAsync();
            var local = await _db.GetFileStateAsync("old-name.txt");

            var remote = RemoteState("new-name.txt", "same content", DateTime.UtcNow.AddMinutes(1), guid: local!.Guid);

            // No download factory on purpose: a move must not need one.
            var action = await _engine.ProcessRemoteFileStateAsync(remote, null);

            Assert.Equal(SyncAction.Moved, action);
            Assert.False(File.Exists(oldPath));
            Assert.Equal("same content", File.ReadAllText(Path.Combine(_root.Path, "new-name.txt")));
            Assert.Equal("new-name.txt", (await _db.GetFileStateByGuidAsync(local.Guid))!.RelativePath);
        }
    }
}
