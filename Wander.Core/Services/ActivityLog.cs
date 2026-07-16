using System;
using System.Collections.Generic;
using System.Linq;

namespace Wander.Core.Services
{
    public record ActivityEntry(DateTime AtUtc, string Category, string Message);

    /// <summary>
    /// In-memory feed of recent sync activity for the dashboard: what changed locally,
    /// what was pulled from whom, conflicts, trash events. Bounded ring buffer — the
    /// durable record is the state database, this is the human-readable window.
    /// </summary>
    public class ActivityLog
    {
        private const int MaxEntries = 200;

        private readonly object _lock = new();
        private readonly Queue<ActivityEntry> _entries = new();

        public event EventHandler<ActivityEntry>? EntryAdded;

        public void Add(string category, string message)
        {
            var entry = new ActivityEntry(DateTime.UtcNow, category, message);
            lock (_lock)
            {
                _entries.Enqueue(entry);
                while (_entries.Count > MaxEntries) _entries.Dequeue();
            }
            EntryAdded?.Invoke(this, entry);
        }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<ActivityEntry> Snapshot()
        {
            lock (_lock)
            {
                return _entries.Reverse().ToList();
            }
        }
    }
}
