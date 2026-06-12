using Microsoft.AspNetCore.Http;

namespace StoreAPI.Cors;

/// <summary>
/// Chrome Private Network Access: pages on localhost calling a private LAN IP (e.g. 192.168.x.x)
/// send <c>Access-Control-Request-Private-Network: true</c> on the CORS preflight.
/// The preflight response must include <c>Access-Control-Allow-Private-Network: true</c>.
/// </summary>
public static class PrivateNetworkAccessMiddleware
{
    public const string AllowPrivateNetworkHeaderName = "Access-Control-Allow-Private-Network";

    public static void UsePrivateNetworkAccessCors(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsOptions(context.Request.Method) &&
                context.Request.Headers.TryGetValue("Access-Control-Request-Private-Network", out var value) &&
                string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.Append(AllowPrivateNetworkHeaderName, "true");
            }

            await next();
        });
    }
}
