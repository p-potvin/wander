using System;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Utils;
using Wander.Protocol;

namespace Wander.Network.Services
{
    public class SyncGrpcService : SyncService.SyncServiceBase
    {
        private const int ChunkSizeBytes = 64 * 1024;

        private readonly StateDatabase _db;
        private readonly WanderOptions _options;

        public SyncGrpcService(StateDatabase db, IOptions<WanderOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
        {
            return Task.FromResult(new PingResponse
            {
                Status = "Online",
                NodeName = _options.NodeName,
                ProtocolVersion = Wander.Core.Services.SyncOrchestrator.ProtocolVersion
            });
        }

        public override async Task ListFiles(ManifestRequest request,
            IServerStreamWriter<FileStateResponse> responseStream, ServerCallContext context)
        {
            foreach (var state in await _db.GetAllStatesAsync())
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await responseStream.WriteAsync(state.ToProto());
            }
        }

        public override async Task<FileStateResponse> GetFileState(StateQueryRequest request, ServerCallContext context)
        {
            var state = await _db.GetFileStateByGuidAsync(request.Guid);
            return state?.ToProto() ?? new FileStateResponse { Exists = false, Guid = request.Guid };
        }

        public override async Task DownloadFile(DownloadRequest request,
            IServerStreamWriter<FileChunk> responseStream, ServerCallContext context)
        {
            var state = await _db.GetFileStateByGuidAsync(request.Guid);
            if (state == null || state.IsDeleted)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"No live file with guid {request.Guid}"));
            }

            var localPath = PathUtils.ToLocalPath(_options.SyncRoot, state.RelativePath);
            if (!File.Exists(localPath))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"File missing on disk: {state.RelativePath}"));
            }

            await using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                ChunkSizeBytes, useAsync: true);

            var buffer = new byte[ChunkSizeBytes];
            long offset = 0;
            int read;
            while ((read = await fs.ReadAsync(buffer, context.CancellationToken)) > 0)
            {
                await responseStream.WriteAsync(new FileChunk
                {
                    Data = ByteString.CopyFrom(buffer, 0, read),
                    Offset = offset,
                    IsFinal = fs.Position >= fs.Length
                });
                offset += read;
            }

            if (offset == 0)
            {
                // Zero-byte file: still emit one terminating chunk so the receiver completes.
                await responseStream.WriteAsync(new FileChunk { Data = ByteString.Empty, Offset = 0, IsFinal = true });
            }
        }
    }
}
