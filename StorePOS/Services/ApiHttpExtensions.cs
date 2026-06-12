using System.Net.Http.Json;
using System.Text.Json;

namespace StorePOS.Services;

internal static class ApiHttpExtensions
{
    private const string StaleServerMessage =
        "Store server is outdated (missing API features). Stop the old StoreAPI.exe, then run the latest build from this project on port 5050.";

    public static async Task<T?> GetApiJsonAsync<T>(
        this HttpClient http,
        string url,
        CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureJsonBody(resp.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<T>(body, AppJson.Options);
    }

    private static void EnsureJsonBody(bool success, string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('<'))
            throw new InvalidOperationException(StaleServerMessage);

        if (!success)
            throw new InvalidOperationException(TrimApiError(body) ?? "Request failed.");
    }

    private static string? TrimApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        return body.Length > 300 ? body[..300] + "…" : body;
    }
}
