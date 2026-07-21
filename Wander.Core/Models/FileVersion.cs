using System;

namespace Wander.Core.Models
{
    /// <summary>One historical version of a file — a row in the FileVersions timeline.</summary>
    public class FileVersion
    {
        public long Id { get; set; }
        public string Guid { get; set; } = string.Empty;         // file identity (matches FileState.Guid)
        public string RelativePath { get; set; } = string.Empty; // path when this version was recorded
        public string Hash { get; set; } = string.Empty;         // content hash == blob key in the VersionStore
        public long SizeBytes { get; set; }
        public DateTime ModifiedUtc { get; set; }                // the file's own mtime for this version
        public string SourceNode { get; set; } = string.Empty;   // node that produced it (local name, or the peer we pulled from)
        public DateTime RecordedUtc { get; set; }                // when Wander captured it
    }
}
