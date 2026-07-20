using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wander.Core.Data;
using Wander.Core.Services;
using Wander.Network;
using Wander.Network.Services;
using Wander.Protocol;
using Xunit;

namespace Wander.Tests
{
    /// <summary>
    /// Two real Wander nodes on loopback: node A serves its files over actual gRPC,
    /// node B pulls with the real orchestrator. Creation, update, delete-to-trash,
    /// and conflict preservation, end to end.
    /// </summary>
    public class TwoNodeIntegrationTests : IAsyncLifetime
    {
        private readonly TempDir _rootA = new();
        private readonly TempDir _rootB = new();
        private readonly TempDir _dbDir = new();

        private StateDatabase _dbA = null!;
        private StateDatabase _dbB = null!;
        private FolderScanner _scannerA = null!;
        private SyncOrchestrator _orchestratorB = null!;
        private WebApplication _nodeA = null!;
        private GrpcChannel _channel = null!;
        private SyncService.SyncServiceClient _clientToA = null!;

        public async Task InitializeAsync()
        {
            _dbA = new StateDatabase(Path.Combine(_dbDir.Path, "a.db"));
            _dbB = new StateDatabase(Path.Combine(_dbDir.Path, "b.db"));
            await _dbA.InitializeAsync();
            await _dbB.InitializeAsync();

            _scannerA = new FolderScanner(_dbA, _rootA.Path);

            var engineB = new SyncEngine(_dbB, _rootB.Path, new TrashService(_rootB.Path), "NodeB");
            _orchestratorB = new SyncOrchestrator(engineB, "NodeB");

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(_dbA);
            builder.Services.AddSingleton<SyncController>(); // node A is never paused in these tests
            builder.Services.Configure<WanderOptions>(o =>
            {
                o.SyncRoot = _rootA.Path;
                o.NodeName = "NodeA";
                o.RequireTailscaleAuth = false;
            });
            builder.WebHost.ConfigureKestrel(k =>
                k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));

            _nodeA = builder.Build();
            _nodeA.MapGrpcService<SyncGrpcService>();
            await _nodeA.StartAsync();

            var address = _nodeA.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            _channel = GrpcChannel.ForAddress(address);
            _clientToA = new SyncService.SyncServiceClient(_channel);
        }

        public async Task DisposeAsync()
        {
            _channel.Dispose();
            await _nodeA.StopAsync();
            await _nodeA.DisposeAsync();
            _rootA.Dispose();
            _rootB.Dispose();
            _dbDir.Dispose();
        }

        [Fact]
        public async Task FullSyncLifecycleAcrossTwoNodes()
        {
            // --- Create: two files appear on A and flow to B ---
            _rootA.WriteFile("notes/readme.md", "hello from node A");
            _rootA.WriteFile("data.bin", "binary-ish payload");
            await _scannerA.ScanAsync();

            var pull = await _orchestratorB.PullFromPeerAsync(_clientToA);

            Assert.Equal("NodeA", pull.PeerName);
            Assert.Equal(2, pull.Downloaded);
            Assert.Equal(0, pull.Errors);
            Assert.Equal("hello from node A", File.ReadAllText(Path.Combine(_rootB.Path, "notes", "readme.md")));

            // --- Idempotence: a second pull does nothing ---
            var second = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(0, second.Downloaded + second.Conflicts + second.Trashed + second.Errors);

            // --- Update: A edits, B fast-forwards without a conflict copy ---
            var fileOnA = Path.Combine(_rootA.Path, "notes", "readme.md");
            File.WriteAllText(fileOnA, "hello again, updated");
            File.SetLastWriteTimeUtc(fileOnA, DateTime.UtcNow.AddMinutes(1));
            await _scannerA.ScanAsync();

            var third = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(1, third.Downloaded);
            Assert.Equal(0, third.Conflicts);
            Assert.Equal("hello again, updated", File.ReadAllText(Path.Combine(_rootB.Path, "notes", "readme.md")));

            // --- Conflict: both edit, A newer → B keeps a conflict copy of its own edit ---
            var fileOnB = Path.Combine(_rootB.Path, "notes", "readme.md");
            File.WriteAllText(fileOnB, "B's competing edit");
            File.SetLastWriteTimeUtc(fileOnB, DateTime.UtcNow.AddMinutes(2));
            File.WriteAllText(fileOnA, "A's winning edit");
            File.SetLastWriteTimeUtc(fileOnA, DateTime.UtcNow.AddMinutes(3));
            await _scannerA.ScanAsync();

            var fourth = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(1, fourth.Conflicts);
            Assert.Equal("A's winning edit", File.ReadAllText(fileOnB));
            var conflictCopy = Directory.GetFiles(Path.Combine(_rootB.Path, "notes")).Single(f => f.Contains("conflict"));
            Assert.Equal("B's competing edit", File.ReadAllText(conflictCopy));
            Assert.Contains("NodeB", conflictCopy);

            // --- Delete: A deletes, B's copy is preserved in trash ---
            File.Delete(Path.Combine(_rootA.Path, "data.bin"));
            await _scannerA.ScanAsync();

            var fifth = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(1, fifth.Trashed);
            Assert.False(File.Exists(Path.Combine(_rootB.Path, "data.bin")));
            var trashed = Directory.GetFiles(Path.Combine(_rootB.Path, ".wander", "trash"), "*", SearchOption.AllDirectories);
            Assert.Contains(trashed, f => f.EndsWith("data.bin"));
        }

        [Fact]
        public async Task ZeroByteFileSyncs()
        {
            _rootA.WriteFile("empty.txt", "");
            await _scannerA.ScanAsync();

            var pull = await _orchestratorB.PullFromPeerAsync(_clientToA);

            Assert.Equal(1, pull.Downloaded);
            Assert.Equal(0, pull.Errors);
            var received = Path.Combine(_rootB.Path, "empty.txt");
            Assert.True(File.Exists(received));
            Assert.Equal(0, new FileInfo(received).Length);
        }

        [Fact]
        public async Task PausedNodeAdvertisesNothing()
        {
            _rootA.WriteFile("secret.txt", "should not leave a paused node");
            await _scannerA.ScanAsync();

            var controllerA = _nodeA.Services.GetRequiredService<SyncController>();
            controllerA.Pause();

            var whilePaused = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(0, whilePaused.FilesConsidered);
            Assert.False(File.Exists(Path.Combine(_rootB.Path, "secret.txt")));

            controllerA.Resume();
            var afterResume = await _orchestratorB.PullFromPeerAsync(_clientToA);
            Assert.Equal(1, afterResume.Downloaded);
            Assert.True(File.Exists(Path.Combine(_rootB.Path, "secret.txt")));
        }
    }
}
