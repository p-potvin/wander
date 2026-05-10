using System.IO;
using System.Security.Cryptography;

namespace Wander.Core.Utils
{
    public static class HashHelper
    {
        public static string ComputeFileHash(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            
            return System.Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
