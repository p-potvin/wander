using System;
using System.IO;

namespace Wander.Core.Utils
{
    /// <summary>
    /// Names the preserved losing version of a sync conflict, attributed to the node
    /// whose edit lost: "report (conflict — Phils-laptop, 2026-07-16 14.30).docx".
    /// The copy is a normal file and syncs to every peer like any other.
    /// </summary>
    public static class ConflictNaming
    {
        public static string BuildConflictPath(string originalFilePath, string losingNodeName, DateTime whenUtc)
        {
            var directory = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(originalFilePath);
            var ext = Path.GetExtension(originalFilePath);
            var node = Sanitize(losingNodeName);
            var stamp = whenUtc.ToString("yyyy-MM-dd HH.mm");

            var candidate = Path.Combine(directory, $"{name} (conflict — {node}, {stamp}){ext}");

            // Same file conflicting twice in one minute: suffix a counter instead of overwriting.
            var counter = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{name} (conflict — {node}, {stamp} #{counter}){ext}");
                counter++;
            }

            return candidate;
        }

        private static string Sanitize(string nodeName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = nodeName.Trim();
            foreach (var c in invalid)
            {
                chars = chars.Replace(c, '-');
            }
            return string.IsNullOrWhiteSpace(chars) ? "unknown-peer" : chars;
        }
    }
}
