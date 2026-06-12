using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace StoreAPI.Cors;

public static class LocalNetworkCors
{
    public const string PolicyName = "LocalNetwork";

    /// <summary>
    /// In Development, allows any origin so Blazor dev servers (any port / https) and tooling work.
    /// Otherwise restricts to localhost and private LAN hosts only.
    /// </summary>
    public static void AddLocalNetworkCors(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
                else
                {
                    policy
                        .SetIsOriginAllowed(IsLocalNetworkOrigin)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });
    }

    private static bool IsLocalNetworkOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "http" && uri.Scheme != "https") return false;

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (IsPrivateIPv4(ip)) return true;
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        // 10.0.0.0/8
        if (bytes[0] == 10) return true;
        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        return false;
    }
}
