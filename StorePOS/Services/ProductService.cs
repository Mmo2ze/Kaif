using System.Net.Http.Json;
using StoreShared.Catalog;

namespace StorePOS.Services;

public sealed class ProductService
{
    private readonly HttpClient _http;

    public ProductService(HttpClient http) => _http = http;

    public async Task<HealthResult?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<HealthDto>("api/health", AppJson.Options, cancellationToken);
            return dto is null ? null : new HealthResult(dto.Status, dto.Time);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(SkuDetailDto? Sku, string? Error)> GetSkuByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return (null, "Enter a barcode.");

        var key = Uri.EscapeDataString(barcode.Trim());
        using var resp = await _http.GetAsync($"api/skus/{key}", cancellationToken);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "No SKU found for this barcode.");
        if (!resp.IsSuccessStatusCode)
            return (null, await resp.Content.ReadAsStringAsync(cancellationToken));

        var sku = await resp.Content.ReadFromJsonAsync<SkuDetailDto>(AppJson.Options, cancellationToken);
        return (sku, null);
    }

    private sealed record HealthDto(string Status, DateTimeOffset Time);
}

public sealed record HealthResult(string Status, DateTimeOffset Time);
