using System;
using System.IO;

namespace Wander.Core.Utils
{
    public static class PathUtils
    {
        public const string WanderDirName = ".wander";

        /// <summary>Relative paths are stored and exchanged with '/' separators on every platform.</summary>
        public static string NormalizeRelative(string relativePath)
        {
            return relativePath.Replace('\\', '/').TrimStart('/');
        }

        public static string ToLocalPath(string syncRoot, string relativePath)
        {
            var combined = Path.GetFullPath(Path.Combine(syncRoot, NormalizeRelative(relativePath)
                .Replace('/', Path.DirectorySeparatorChar)));

            // Guard against path traversal from a malicious or corrupt manifest entry.
            var root = Path.GetFullPath(syncRoot);
            if (!combined.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing path outside sync root: {relativePath}");
            }

            return combined;
        }

        public static string ToRelativePath(string syncRoot, string localPath)
        {
            return NormalizeRelative(Path.GetRelativePath(syncRoot, localPath));
        }

        /// <summary>Wander's own bookkeeping (state db, trash) lives under .wander/ and is never synced.</summary>
        public static bool IsInternal(string relativePath)
        {
            var normalized = NormalizeRelative(relativePath);
            return normalized.Equals(WanderDirName, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(WanderDirName + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
