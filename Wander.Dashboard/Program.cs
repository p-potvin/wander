using System;
using Avalonia;
using Microsoft.AspNetCore.Builder;
using Velopack;
using Wander.Network;

namespace Wander.Dashboard
{
    internal static class Program
    {
        /// <summary>The in-process Wander node the dashboard observes. One process, one node.</summary>
        public static WebApplication Node { get; private set; } = null!;

        /// <summary>Background auto-updater, exposed so the tray can trigger "apply and restart".</summary>
        public static UpdateService Updates { get; private set; } = null!;

        [STAThread]
        public static void Main(string[] args)
        {
            // MUST be first: Velopack's install/update/uninstall hooks run here and may
            // exit the process before we ever bind a port or spin up the sync engine.
            VelopackApp.Build().Run();

            Node = WanderHost.Build(args);
            Node.StartAsync().GetAwaiter().GetResult();

            Updates = UpdateService.FromServices(Node.Services);
            _ = Updates.CheckAsync(); // fire-and-forget; never blocks startup

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                Node.StopAsync().GetAwaiter().GetResult();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
