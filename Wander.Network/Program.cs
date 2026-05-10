using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wander.Network.Services;

namespace Wander.Network
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddGrpc();
            
            // Basic auth or token validation can be added here later

            var tailscaleIp = GetTailscaleIpAddress();
            var port = 5555; // Default Wander sync port

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                if (tailscaleIp != null)
                {
                    Console.WriteLine($"Binding to Tailscale IP: {tailscaleIp}:{port}");
                    serverOptions.Listen(tailscaleIp, port);
                }
                else
                {
                    Console.WriteLine("Warning: Tailscale interface not found. Binding to Localhost for fallback.");
                    serverOptions.ListenLocalhost(port);
                }
            });

            var app = builder.Build();

            app.UseRouting();
            app.MapGrpcService<SyncGrpcService>();

            app.Run();
        }

        private static IPAddress? GetTailscaleIpAddress()
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
                        var ipProps = iface.GetIPProperties();
                        var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(addr => 
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
