using System;
using System.Net;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Wander.Network.Services
{
    /// <summary>
    /// Wander's auth layer is the tailnet itself: an incoming call is accepted only if
    /// Tailscale can resolve the caller's IP to a tailnet identity (WhoIs). No accounts,
    /// no pairing codes. Loopback can be allowed for local testing.
    /// </summary>
    public class TailscaleAuthInterceptor : Interceptor
    {
        public const string IdentityHttpContextKey = "Wander.TailscaleIdentity";

        private readonly TailscaleService _tailscale;
        private readonly WanderOptions _options;

        public TailscaleAuthInterceptor(TailscaleService tailscale, IOptions<WanderOptions> options)
        {
            _tailscale = tailscale;
            _options = options.Value;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            await AuthenticateAsync(context);
            return await continuation(request, context);
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            await AuthenticateAsync(context);
            await continuation(request, responseStream, context);
        }

        private async Task AuthenticateAsync(ServerCallContext context)
        {
            if (!_options.RequireTailscaleAuth) return;

            var httpContext = context.GetHttpContext();
            var remoteIp = httpContext.Connection.RemoteIpAddress;

            if (remoteIp == null)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Caller address unknown."));
            }

            if (IPAddress.IsLoopback(remoteIp))
            {
                if (_options.AllowLoopback) return;
                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    "Loopback callers are not allowed (set Wander:AllowLoopback for local testing)."));
            }

            var ip = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            var identity = await _tailscale.WhoIsAsync(ip.ToString(), context.CancellationToken);

            if (identity == null)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    $"Caller {ip} is not a recognized tailnet member."));
            }

            httpContext.Items[IdentityHttpContextKey] = identity;
        }
    }
}
