using System;
using System.IO;
using Wander.Core.Utils;

namespace Wander.Core.Services
{
    /// <summary>
    /// Deletes are never silent: files removed by a remote peer land in
    /// .wander/trash/&lt;utc-timestamp&gt;/&lt;relative-path&gt; and are purged after the
    /// retention window (30 days by default).
    /// </summary>
    public class TrashService
    {
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
        private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

        private readonly string _syncRootPath;
        private readonly TimeSpan _retention;

        public string TrashRootPath { get; }

        public TrashService(string syncRootPath, TimeSpan? retention = null)
        {
            _syncRootPath = syncRootPath;
            _retention = retention ?? DefaultRetention;
            TrashRootPath = Path.Combine(syncRootPath, PathUtils.WanderDirName, "trash");
        }

        /// <returns>The path the file was preserved at.</returns>
        public string MoveToTrash(string localFilePath, DateTime whenUtc)
        {
            var relativePath = PathUtils.ToRelativePath(_syncRootPath, localFilePath);
            var destination = Path.Combine(TrashRootPath, whenUtc.ToString(TimestampFormat),
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(localFilePath, destination, overwrite: true);
            return destination;
        }

        /// <summary>Removes trash batches older than the retention window.</summary>
        public int PurgeExpired(DateTime nowUtc)
        {
            if (!Directory.Exists(TrashRootPath)) return 0;

            var purged = 0;
            foreach (var batchDir in Directory.EnumerateDirectories(TrashRootPath))
            {
                var name = Path.GetFileName(batchDir);
                if (!DateTime.TryParseExact(name, TimestampFormat, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var batchTime))
                {
                    continue; // not ours; leave it alone
                }

                if (nowUtc - batchTime > _retention)
                {
                    Directory.Delete(batchDir, recursive: true);
                    purged++;
                }
            }

            return purged;
        }
    }
}
