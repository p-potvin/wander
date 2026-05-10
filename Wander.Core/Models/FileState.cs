using System;

namespace Wander.Core.Models
{
    public class FileState
    {
        public string Guid { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }
}
