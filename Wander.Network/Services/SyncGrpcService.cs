using Grpc.Core;
using Wander.Network.Protos;
using System.Threading.Tasks;

namespace Wander.Network.Services
{
    public class SyncGrpcService : SyncService.SyncServiceBase
    {
        public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
        {
            return Task.FromResult(new PingResponse
            {
                Status = "Online",
                NodeName = "WanderPeer"
            });
        }

        public override Task<FileStateResponse> GetFileState(StateQueryRequest request, ServerCallContext context)
        {
            // Placeholder: Wire up to Wander.Core.Data.StateDatabase
            return Task.FromResult(new FileStateResponse
            {
                Exists = true,
                Guid = request.Guid,
                RelativePath = "mock/file.txt",
                Hash = "mock-hash"
            });
        }

        public override async Task DownloadFile(DownloadRequest request, IServerStreamWriter<FileChunk> responseStream, ServerCallContext context)
        {
            // Placeholder: Stream file in chunks
            byte[] mockData = new byte[1024]; // 1KB mock chunk
            
            await responseStream.WriteAsync(new FileChunk
            {
                Data = Google.Protobuf.ByteString.CopyFrom(mockData),
                Offset = 0,
                IsFinal = true
            });
        }
    }
}
