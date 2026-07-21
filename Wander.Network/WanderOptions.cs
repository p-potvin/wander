using System;

namespace Wander.Network
{
    public class WanderOptions
    {
        public const string SectionName = "Wander";

        public string SyncRoot { get; set; } =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WanderSync");

        /// <summary>Defaults to &lt;SyncRoot&gt;/.wander/state.db when empty.</summary>
        public string DbPath { get; set; } = string.Empty;

        public int Port { get; set; } = 5555;

        public string NodeName { get; set; } = Environment.MachineName;

        /// <summary>Reject callers whose IP can't be resolved to a tailnet identity.</summary>
        public bool RequireTailscaleAuth { get; set; } = true;

        /// <summary>Accept loopback callers (local testing, two nodes on one machine).</summary>
        public bool AllowLoopback { get; set; } = false;

        /// <summary>Extra peers to pull from, e.g. "http://127.0.0.1:5556" (dev/testing).</summary>
        public string[] StaticPeers { get; set; } = Array.Empty<string>();

        public int PullIntervalSeconds { get; set; } = 30;

        public int TrashRetentionDays { get; set; } = 30;

        /// <summary>Max versions kept per file in "Wander back in time" before A.N.S.W.E.R.S. thins.</summary>
        public int MaxVersionsPerFile { get; set; } = 10;

        /// <summary>
        /// Velopack update feed (a URL or local/UNC path to the release folder). When empty,
        /// auto-update is disabled — useful for dev runs and until a release feed is published.
        /// </summary>
        public string UpdateUrl { get; set; } = string.Empty;

        public string ResolvedDbPath => string.IsNullOrWhiteSpace(DbPath)
            ? System.IO.Path.Combine(SyncRoot, ".wander", "state.db")
            : DbPath;
    }
}
