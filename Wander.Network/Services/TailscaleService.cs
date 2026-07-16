using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wander.Network.Services
{
    public record TailscalePeer(string HostName, string DnsName, string Ip, bool Online)
    {
        /// <summary>
        /// The name to show a human. HostName is self-reported by the device's OS and is
        /// unreliable (an iPad reports "localhost"; a server named "greencloud-vps" in the
        /// tailnet admin console can report its internal OS hostname "vaultwares" instead).
        /// DNSName is Tailscale's own assigned name and matches what the admin console and
        /// `tailscale status` show, so it's the name that matches VaultWares' naming convention.
        /// </summary>
        public string DisplayName
        {
            get
            {
                var label = DnsName.TrimEnd('.').Split('.')[0];
                return !string.IsNullOrWhiteSpace(label) ? label : HostName;
            }
        }
    }

    public record TailscaleIdentity(string LoginName, string NodeName);

    /// <summary>
    /// Wander's window into the tailnet, via the Tailscale CLI (`status`/`whois`).
    /// Discovery: which nodes exist and are online. Identity: which tailnet user is
    /// behind an incoming connection — this is Wander's entire auth layer.
    /// </summary>
    public class TailscaleService
    {
        private static readonly TimeSpan WhoIsCacheTtl = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, (TailscaleIdentity? Identity, DateTime CachedAtUtc)> _whoIsCache = new();

        public virtual async Task<IReadOnlyList<TailscalePeer>> GetOnlinePeersAsync(CancellationToken ct = default)
        {
            var json = await RunTailscaleAsync("status --json", ct);
            if (json == null) return Array.Empty<TailscalePeer>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Peer", out var peers)) return Array.Empty<TailscalePeer>();

                var result = new List<TailscalePeer>();
                foreach (var peer in peers.EnumerateObject())
                {
                    var p = peer.Value;
                    var online = p.TryGetProperty("Online", out var o) && o.GetBoolean();
                    var ips = p.TryGetProperty("TailscaleIPs", out var ipsEl)
                        ? ipsEl.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList()
                        : new List<string?>();
                    var ipv4 = ips.FirstOrDefault(ip => ip!.Contains('.'));
                    if (ipv4 == null) continue;

                    result.Add(new TailscalePeer(
                        HostName: p.TryGetProperty("HostName", out var h) ? h.GetString() ?? "" : "",
                        DnsName: p.TryGetProperty("DNSName", out var d) ? d.GetString() ?? "" : "",
                        Ip: ipv4!,
                        Online: online));
                }

                return result.Where(p => p.Online).ToList();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Tailscale] Could not parse status output: {ex.Message}");
                return Array.Empty<TailscalePeer>();
            }
        }

        public virtual async Task<TailscaleIdentity?> WhoIsAsync(string ip, CancellationToken ct = default)
        {
            if (_whoIsCache.TryGetValue(ip, out var cached) && DateTime.UtcNow - cached.CachedAtUtc < WhoIsCacheTtl)
            {
                return cached.Identity;
            }

            var json = await RunTailscaleAsync($"whois --json {ip}", ct);
            TailscaleIdentity? identity = null;

            if (json != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var login = doc.RootElement.TryGetProperty("UserProfile", out var up)
                                && up.TryGetProperty("LoginName", out var l) ? l.GetString() : null;
                    var node = doc.RootElement.TryGetProperty("Node", out var n)
                               && n.TryGetProperty("Name", out var nn) ? nn.GetString() : null;

                    if (login != null || node != null)
                    {
                        identity = new TailscaleIdentity(login ?? "unknown", node ?? ip);
                    }
                }
                catch (JsonException)
                {
                    // fall through: unidentified
                }
            }

            _whoIsCache[ip] = (identity, DateTime.UtcNow);
            return identity;
        }

        private static async Task<string?> RunTailscaleAsync(string arguments, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tailscale",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                return process.ExitCode == 0 ? output : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tailscale] CLI unavailable ({ex.GetType().Name}): {ex.Message}");
                return null;
            }
        }
    }
}
