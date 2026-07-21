using System.Collections.Generic;
using System.Linq;
using Wander.Core.Services.Retention;
using Xunit;

namespace Wander.Tests
{
    public class AnswersRetentionTests
    {
        private static List<VersionRef> Versions(int count)
        {
            var baseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // Distinct days, id N is the newest.
            return Enumerable.Range(1, count)
                .Select(i => new VersionRef(i, baseUtc.AddDays(i)))
                .ToList();
        }

        [Fact]
        public void KeepsEverythingUnderBudget()
        {
            var policy = new AnswersRetention();
            Assert.Empty(policy.SelectEvictions("f", Versions(6), 6));
            Assert.Empty(policy.SelectEvictions("f", Versions(3), 6));
        }

        [Fact]
        public void EvictsExactlyOnePerInsertOverBudget()
        {
            var policy = new AnswersRetention();
            var evicted = policy.SelectEvictions("f", Versions(7), 6);
            Assert.Single(evicted);
        }

        [Fact]
        public void NeverEvictsNewestOrOldest()
        {
            var policy = new AnswersRetention();
            var versions = Versions(21);
            var newest = versions.OrderByDescending(v => v.RecordedUtc).First().Id;
            var oldest = versions.OrderBy(v => v.RecordedUtc).First().Id;

            var evicted = policy.SelectEvictions("f", versions, 20);

            Assert.DoesNotContain(newest, evicted);
            Assert.DoesNotContain(oldest, evicted);
        }

        [Fact]
        public void RollingInsertionsHoldAtCapAndPreserveNewestAndOldest()
        {
            const int max = 10;
            var policy = new AnswersRetention();
            var baseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var live = new List<VersionRef>();
            long nextId = 1;
            long oldestId = 1;

            // Simulate 200 edits over 200 distinct days.
            for (var day = 0; day < 200; day++)
            {
                var v = new VersionRef(nextId, baseUtc.AddDays(day));
                if (day == 0) oldestId = v.Id;
                live.Add(v);
                nextId++;

                foreach (var id in policy.SelectEvictions("f", live, max))
                {
                    live.RemoveAll(x => x.Id == id);
                }

                Assert.True(live.Count <= max, $"count {live.Count} exceeded cap on day {day}");
                Assert.Contains(live, x => x.Id == v.Id);        // newest always survives
                Assert.Contains(live, x => x.Id == oldestId);    // the very first version is never thinned away
            }

            Assert.Equal(max, live.Count);
        }

        [Fact]
        public void IsDeterministic()
        {
            var a = new AnswersRetention().SelectEvictions("f", Versions(15), 12);
            var b = new AnswersRetention().SelectEvictions("f", Versions(15), 12);
            Assert.Equal(a, b);
        }

        [Fact]
        public void RejectsTinyBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AnswersRetention().SelectEvictions("f", Versions(3), 1));
        }
    }
}
