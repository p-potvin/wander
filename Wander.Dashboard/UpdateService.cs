using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Velopack;
using Wander.Core.Services;
using Wander.Network;

namespace Wander.Dashboard
{
    /// <summary>
    /// Background auto-update via Velopack. Checks the configured feed on startup, downloads
    /// any newer release, and surfaces it through the activity feed + a "restart to apply"
    /// affordance — it never restarts a running sync node out from under the user.
    /// A no-op when no feed is configured (dev runs, or before a release feed exists).
    /// </summary>
    public class UpdateService
    {
        private readonly string _url;
        private readonly ActivityLog _activity;
        private UpdateManager? _manager;
        private UpdateInfo? _pending;

        /// <summary>Raised (once) when an update has finished downloading and is ready to apply.</summary>
        public event EventHandler? UpdateReady;

        public bool HasPendingUpdate => _pending != null;

        public UpdateService(string url, ActivityLog activity)
        {
            _url = url;
            _activity = activity;
        }

        public static UpdateService FromServices(IServiceProvider services)
        {
            var options = services.GetRequiredService<IOptions<WanderOptions>>().Value;
            return new UpdateService(options.UpdateUrl, services.GetRequiredService<ActivityLog>());
        }

        public async Task CheckAsync()
        {
            if (string.IsNullOrWhiteSpace(_url)) return;

            try
            {
                // A github.com repo URL uses the Releases feed; anything else is treated as a
                // plain URL or local/UNC path to a folder hosting the RELEASES metadata.
                _manager = _url.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                    ? new UpdateManager(new Velopack.Sources.GithubSource(_url, null, prerelease: false))
                    : new UpdateManager(_url);

                // Running from `dotnet run` or an xcopy folder isn't a Velopack install; nothing to update.
                if (!_manager.IsInstalled) return;

                var info = await _manager.CheckForUpdatesAsync();
                if (info == null) return;

                var version = info.TargetFullRelease.Version;
                _activity.Add("update", $"Update {version} available — downloading in the background…");
                await _manager.DownloadUpdatesAsync(info);

                _pending = info;
                _activity.Add("update", $"Update {version} ready — restart Wander to apply it");
                UpdateReady?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _activity.Add("error", $"Update check failed: {ex.Message}");
            }
        }

        /// <summary>Applies the downloaded update and relaunches. No-op if nothing is staged.</summary>
        public void ApplyAndRestart()
        {
            if (_manager != null && _pending != null)
            {
                _manager.ApplyUpdatesAndRestart(_pending);
            }
        }
    }
}
