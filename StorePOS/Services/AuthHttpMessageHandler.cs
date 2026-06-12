using System.Net.Http.Headers;

namespace StorePOS.Services;

public sealed class AuthHttpMessageHandler : DelegatingHandler
{
    private readonly AppState _app;

    public AuthHttpMessageHandler(AppState app)
    {
        _app = app;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_app.Token is { Length: > 0 } t)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        return base.SendAsync(request, cancellationToken);
    }
}
