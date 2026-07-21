using System.IO;
using System.Threading.Tasks;
using Wander.Core.Utils;

namespace Wander.Core.Services
{
    /// <summary>
    /// Content-addressed blob store for file versions, under .wander/versions.
    /// A version's content is stored once under its hash (sharded by the first two hex
    /// chars); identical content across versions or files dedupes for free. Restoring a
    /// version copies its blob back to the working tree.
    /// </summary>
    public class VersionStore
    {
        private readonly string _root;

        public VersionStore(string syncRootPath)
        {
            _root = Path.Combine(syncRootPath, PathUtils.WanderDirName, "versions");
        }

        public string BlobPath(string hash) => Path.Combine(_root, hash[..2], hash);

        public bool Exists(string hash) => hash.Length >= 2 && File.Exists(BlobPath(hash));

        /// <summary>Copies the file's current content into the store keyed by its (already computed) hash.</summary>
        public async Task StoreAsync(string sourceFilePath, string hash)
        {
            if (hash.Length < 2 || Exists(hash)) return; // already have this content

            var dest = BlobPath(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            var temp = dest + ".tmp";
            await using (var src = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await src.CopyToAsync(dst);
            }
            File.Move(temp, dest, overwrite: true);
        }

        /// <summary>Copies a stored version back out to a destination path.</summary>
        public async Task RestoreToAsync(string hash, string destinationPath)
        {
            var blob = BlobPath(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using var src = new FileStream(blob, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await src.CopyToAsync(dst);
        }

        /// <summary>Removes a blob once no history row references its hash (called after eviction).</summary>
        public void DeleteBlobIfUnreferenced(string hash, bool stillReferenced)
        {
            if (stillReferenced) return;
            var blob = BlobPath(hash);
            if (File.Exists(blob)) File.Delete(blob);
        }
    }
}
