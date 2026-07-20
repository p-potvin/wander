using System;
using System.Threading;

namespace Wander.Core.Services
{
    /// <summary>
    /// Shared pause switch for a node. When paused, the node goes silent in both
    /// directions: the daemon stops pulling from peers, and the gRPC service stops
    /// advertising its manifest (so peers pulling from us see nothing new). The local
    /// watcher keeps indexing throughout, so resuming picks up instantly with no rescan.
    /// </summary>
    public class SyncController
    {
        private int _paused; // 0 = running, 1 = paused (Interlocked for lock-free reads on the hot path)

        public event EventHandler<bool>? PausedChanged;

        public bool IsPaused => Volatile.Read(ref _paused) == 1;

        public void Pause() => Set(true);

        public void Resume() => Set(false);

        public bool Toggle()
        {
            var nowPaused = !IsPaused;
            Set(nowPaused);
            return nowPaused;
        }

        private void Set(bool paused)
        {
            var previous = Interlocked.Exchange(ref _paused, paused ? 1 : 0);
            if ((previous == 1) != paused)
            {
                PausedChanged?.Invoke(this, paused);
            }
        }
    }
}
