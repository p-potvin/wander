using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Wander.Core.Services.Retention
{
    /// <summary>
    /// A.N.S.W.E.R.S. — the Alternating, Non-Sequential (…With Encryption, Restoring) System.
    /// The user's own "middle-out" backup-rotation algorithm (see ANSWERS_backup_algo.md),
    /// applied here per file: keep at most N versions; once full, each new version evicts one
    /// existing version by alternating inward from the front and back of the timeline, so the
    /// very newest and very oldest are always preserved while the middle is thinned. The intent
    /// is to stretch the span of history covered while keeping recent versions dense.
    ///
    /// Interpretation of the spec (FLAGGED for owner sign-off — some steps are ambiguous):
    ///   Versions are ordered newest-first (index 0 = newest), matching the spec's "insert x
    ///   at the front" example. On an eviction:
    ///     skipValue     = sameDayCount + skipModifier   (skipModifier starts at 1)
    ///     sameDayCount   = versions (incl. the just-added one) sharing the newest version's date
    ///     if sameDayCount is odd  -> evict from the back  at index (n - 1 - skipValue)
    ///     else                    -> evict from the front at index (skipValue)
    ///   The chosen index is clamped to [1, n-2] so index 0 (newest) and index n-1 (oldest)
    ///   are never evicted. skipModifier increments each eviction and resets to 1 once
    ///   skipValue >= N/2 - 1, so the eviction point cycles through the interior over time.
    ///   State (skipModifier) is tracked per file key, matching "one rotating set per file."
    ///
    /// The spec's 10-item worked example ([1..0] + x -> [x,1,3,4,5,6,7,8,9,0], i.e. "2" removed)
    /// is pinned in the tests so any reinterpretation is caught.
    /// </summary>
    public class AnswersRetention : IRetentionPolicy
    {
        private readonly int _defaultSkipModifier;
        private readonly ConcurrentDictionary<string, int> _skipModifier = new();

        public AnswersRetention(int defaultSkipModifier = 1)
        {
            _defaultSkipModifier = Math.Max(1, defaultSkipModifier);
        }

        public IReadOnlyList<long> SelectEvictions(string fileKey, IReadOnlyList<VersionRef> versions, int maxVersions)
        {
            if (maxVersions < 2) throw new ArgumentOutOfRangeException(nameof(maxVersions), "A.N.S.W.E.R.S. needs to keep at least 2 versions (newest + oldest).");
            if (versions.Count <= maxVersions) return Array.Empty<long>();

            // Newest-first working copy.
            var timeline = versions.OrderByDescending(v => v.RecordedUtc).ThenByDescending(v => v.Id).ToList();
            var evicted = new List<long>();

            // Evict until within budget (normally one pass; more if maxVersions was lowered).
            while (timeline.Count > maxVersions)
            {
                var n = timeline.Count;
                var newestDate = timeline[0].RecordedUtc.Date;
                var sameDayCount = timeline.Count(v => v.RecordedUtc.Date == newestDate);
                var skipModifier = _skipModifier.GetOrAdd(fileKey, _defaultSkipModifier);
                var skipValue = sameDayCount + skipModifier;

                int index = (sameDayCount % 2 == 1)
                    ? n - 1 - skipValue   // odd  -> from the back
                    : skipValue;          // even -> from the front

                // Never evict the absolute newest (0) or oldest (n-1).
                index = Math.Clamp(index, 1, n - 2);

                evicted.Add(timeline[index].Id);
                timeline.RemoveAt(index);

                // Advance the cycle; reset once the skip reaches the midpoint.
                skipModifier++;
                if (skipValue >= (maxVersions / 2) - 1) skipModifier = _defaultSkipModifier;
                _skipModifier[fileKey] = skipModifier;
            }

            return evicted;
        }
    }
}
