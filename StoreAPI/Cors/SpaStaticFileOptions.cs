using Microsoft.AspNetCore.Builder;
using Microsoft.Net.Http.Headers;

namespace StoreAPI.Cors;

internal static class SpaStaticFileOptions
{
    /// <summary>Hashed assets can be cached; SPA shell must not (avoids stale index.html on phones after updates).</summary>
    public static StaticFileOptions Create() =>
        new()
        {
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path.Value ?? "";
                if (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/lan-test.html", StringComparison.OrdinalIgnoreCase) ||
                    (!path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) &&
                     !path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) &&
                     path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                {
                    var headers = ctx.Context.Response.Headers;
                    headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
                    headers[HeaderNames.Pragma] = "no-cache";
                    headers[HeaderNames.Expires] = "0";
                }
            },
        };
}
