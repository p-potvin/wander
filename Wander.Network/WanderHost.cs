using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wander.Core.Data;
using Wander.Core.Services;
using Wander.Network.Services;

namespace Wander.Network
{
    /// <summary>
    /// Builds a complete Wander node (engine + gRPC endpoint + sync daemon) as a
    /// WebApplication. Used by the headless Wander.Network.exe and hosted in-process
    /// by the dashboard — one node, two frontends.
    /// </summary>
    public static class WanderHost
    {
        public static WebApplication Build(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<WanderOptions>(builder.Configuration.GetSection(WanderOptions.SectionName));

            // A node is one sync root + one state db + one engine; everything is a singleton.
            builder.Services.AddSingleton<ActivityLog>();
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                Directory.CreateDirectory(Path.GetDirectoryName(options.ResolvedDbPath)!);
                return new StateDatabase(options.ResolvedDbPath);
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                return new TrashService(options.SyncRoot, TimeSpan.FromDays(options.TrashRetentionDays));
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                return new SyncEngine(sp.GetRequiredService<StateDatabase>(), options.SyncRoot,
                    sp.GetRequiredService<TrashService>(), options.NodeName);
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                return new FolderScanner(sp.GetRequiredService<StateDatabase>(), options.SyncRoot);
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                return new LocalIndexer(sp.GetRequiredService<StateDatabase>(), options.SyncRoot);
            });
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WanderOptions>>().Value;
                return new SyncOrchestrator(sp.GetRequiredService<SyncEngine>(), options.NodeName);
            });
            builder.Services.AddSingleton<TailscaleService>();
            builder.Services.AddSingleton<TailscaleAuthInterceptor>();
            builder.Services.AddHostedService<SyncDaemon>();

            builder.Services.AddGrpc(grpc =>
            {
                grpc.Interceptors.Add<TailscaleAuthInterceptor>();
            });

            var options = builder.Configuration.GetSection(WanderOptions.SectionName).Get<WanderOptions>() ?? new WanderOptions();
            var tailscaleIp = GetTailscaleIpAddress();

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                // gRPC over cleartext HTTP/2: the tailnet is the encryption layer (WireGuard).
                if (tailscaleIp != null)
                {
                    Console.WriteLine($"Binding to Tailscale IP: {tailscaleIp}:{options.Port}");
                    serverOptions.Listen(tailscaleIp, options.Port, o => o.Protocols = HttpProtocols.Http2);
                }
                else
                {
                    Console.WriteLine("Warning: Tailscale interface not found. Binding to localhost for fallback.");
                    serverOptions.ListenLocalhost(options.Port, o => o.Protocols = HttpProtocols.Http2);
                }

                if (tailscaleIp != null && options.AllowLoopback)
                {
                    serverOptions.ListenLocalhost(options.Port, o => o.Protocols = HttpProtocols.Http2);
                }
            });

            var app = builder.Build();
            app.MapGrpcService<SyncGrpcService>();
            return app;
        }

        public static IPAddress? GetTailscaleIpAddress()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    // Tailscale interface usually has "Tailscale" in name or description
                    if (iface.OperationalStatus == OperationalStatus.Up &&
                       (iface.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                        iface.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)))
                    {
                        var ipv4 = iface.GetIPProperties().UnicastAddresses.FirstOrDefault(addr =>
                            addr.Address.AddressFamily == AddressFamily.InterNetwork);

                        if (ipv4 != null)
                        {
                            return ipv4.Address;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving network interfaces: {ex.Message}");
            }

            return null;
        }
    }
}
