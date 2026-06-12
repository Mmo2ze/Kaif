using System.Net.Http.Json;
using StoreShared.Auth;
using StoreShared.Pos;
using StoreShared.Sales;
using StoreShared.Stock;

namespace StorePOS.Services;

public sealed class SaleService
{
    private readonly HttpClient _http;

    public SaleService(HttpClient http) => _http = http;

    public async Task<PosSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<PosSettingsDto>("api/settings", AppJson.Options, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> AuthorizeDiscountAsync(string pin, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/pos/authorize-discount",
            new AuthorizeDiscountRequest(pin),
            AppJson.Options,
            cancellationToken);
        if (!resp.IsSuccessStatusCode)
            return null;
        var body = await resp.Content.ReadFromJsonAsync<AuthorizeDiscountResponse>(AppJson.Options, cancellationToken);
        return body?.DiscountAuthorizationToken;
    }

    public async Task<(SaleCreatedDto? Result, string? Error)> CreateSaleAsync(
        CreateSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/sales", request, AppJson.Options, cancellationToken);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<SaleCreatedDto>(AppJson.Options, cancellationToken);
            return (dto, null);
        }

        var err = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (null, string.IsNullOrWhiteSpace(err) ? resp.ReasonPhrase : err);
    }

    public async Task<SalesSummaryDto?> GetSalesSummaryAsync(
        DateOnly from,
        DateOnly to,
        int? sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/sales/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (sellerUserId is not null)
            url += $"&sellerUserId={sellerUserId.Value}";
        return await _http.GetFromJsonAsync<SalesSummaryDto>(url, AppJson.Options, cancellationToken);
    }

    public async Task<PagedSalesResult?> GetSalesHistoryAsync(
        DateOnly from,
        DateOnly to,
        int? sellerUserId,
        int page,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/sales?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&page={page}&pageSize={pageSize}";
        if (sellerUserId is not null)
            url += $"&sellerUserId={sellerUserId.Value}";
        return await _http.GetFromJsonAsync<PagedSalesResult>(url, AppJson.Options, cancellationToken);
    }

    public async Task<SaleHistoryDetailDto?> GetSaleDetailAsync(int saleId, CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<SaleHistoryDetailDto>($"api/sales/{saleId}", AppJson.Options, cancellationToken);

    public async Task<IReadOnlyList<UserListItemDto>?> GetStaffForFilterAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<List<UserListItemDto>>("api/auth/staff", AppJson.Options, cancellationToken);

    public async Task<(byte[]? Bytes, string? Error)> ExportSalesCsvAsync(
        DateOnly from,
        DateOnly to,
        int? sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/sales/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (sellerUserId is not null)
            url += $"&sellerUserId={sellerUserId.Value}";
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
            return (null, await resp.Content.ReadAsStringAsync(cancellationToken));
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
        return (bytes, null);
    }
}
