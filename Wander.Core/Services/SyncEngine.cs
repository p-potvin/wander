using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Wander.Core.Data;
using Wander.Core.Models;
using Wander.Core.Utils;
using Wander.Network.Protos;

namespace Wander.Core.Services
{
    public class SyncEngine
    {
        private readonly StateDatabase _db;
        private readonly string _syncRootPath;

        public SyncEngine(StateDatabase db, string syncRootPath)
        {
            _db = db;
            _syncRootPath = syncRootPath;
        }

        public async Task ProcessRemoteFileStateAsync(FileState remoteState, AsyncServerStreamingCall<FileChunk>? downloadStream)
        {
            var localFilePath = Path.Combine(_syncRootPath, remoteState.RelativePath);
            var localState = await _db.GetFileStateByGuidAsync(remoteState.Guid);

            bool needsDownload = false;

            if (!File.Exists(localFilePath))
            {
                needsDownload = true;
            }
            else
            {
                var currentLocalHash = HashHelper.ComputeFileHash(localFilePath);
                if (currentLocalHash != remoteState.Hash)
                {
                    var offlineEditPath = GetOfflineEditPath(localFilePath);
                    File.Move(localFilePath, offlineEditPath, overwrite: true);
                    Console.WriteLine($"[Conflict] Renamed local offline edit to: {offlineEditPath}");
                    needsDownload = true;
                }
            }

            if (needsDownload && downloadStream != null)
            {
                // Streaming chunk assembly to prevent massive RAM spikes
                using var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                
                await foreach (var chunk in downloadStream.ResponseStream.ReadAllAsync())
                {
                    await fs.WriteAsync(chunk.Data.Memory);
                    if (chunk.IsFinal) break;
                }

                await _db.UpsertFileStateAsync(new FileState
                {
                    Guid = remoteState.Guid,
                    RelativePath = remoteState.RelativePath,
                    Hash = remoteState.Hash,
                    SizeBytes = remoteState.SizeBytes,
                    LastModified = remoteState.LastModified,
                    IsDeleted = false
                });

                Console.WriteLine($"[Sync] Successfully streamed remote file: {remoteState.RelativePath}");
            }
        }

        private string GetOfflineEditPath(string originalFilePath)
        {
            var directory = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalFilePath);
            var ext = Path.GetExtension(originalFilePath);

            return Path.Combine(directory, $"{fileNameWithoutExt} (offline-edit){ext}");
        }
    }
}
