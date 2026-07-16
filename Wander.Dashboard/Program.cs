using System;
using Avalonia;
using Microsoft.AspNetCore.Builder;
using Wander.Network;

namespace Wander.Dashboard
{
    internal static class Program
    {
        /// <summary>The in-process Wander node the dashboard observes. One process, one node.</summary>
        public static WebApplication Node { get; private set; } = null!;

        [STAThread]
        public static void Main(string[] args)
        {
            Node = WanderHost.Build(args);
            Node.StartAsync().GetAwaiter().GetResult();

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
