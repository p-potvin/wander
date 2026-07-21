using System;
using System.Collections.Generic;

namespace Wander.Core.Services.Retention
{
    /// <summary>One tracked version, as the retention policy sees it.</summary>
    public readonly record struct VersionRef(long Id, DateTime RecordedUtc);

    /// <summary>
    /// Decides which stored versions of a single file to evict to stay within budget.
    /// The version store and history are policy-agnostic; swap the policy here without
    /// touching anything else.
    /// </summary>
    public interface IRetentionPolicy
    {
        /// <summary>
        /// Given a file's versions (any order) and the max to keep, return the Ids to evict.
        /// Called after each new version is appended, so it normally returns 0 or 1 Id.
        /// </summary>
        IReadOnlyList<long> SelectEvictions(string fileKey, IReadOnlyList<VersionRef> versions, int maxVersions);
    }
}
