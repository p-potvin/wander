using System;
using System.Globalization;
using Wander.Protocol;

namespace Wander.Core.Models
{
    public static class FileStateProtoExtensions
    {
        public static FileStateResponse ToProto(this FileState state)
        {
            return new FileStateResponse
            {
                Exists = true,
                Guid = state.Guid,
                RelativePath = state.RelativePath,
                SizeBytes = state.SizeBytes,
                LastModified = state.LastModified.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Hash = state.Hash,
                IsDeleted = state.IsDeleted
            };
        }

        public static FileState ToFileState(this FileStateResponse response)
        {
            return new FileState
            {
                Guid = response.Guid,
                RelativePath = response.RelativePath,
                SizeBytes = response.SizeBytes,
                LastModified = DateTime.Parse(response.LastModified, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind).ToUniversalTime(),
                Hash = response.Hash,
                IsDeleted = response.IsDeleted
            };
        }
    }
}
