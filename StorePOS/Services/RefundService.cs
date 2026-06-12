using System.Net.Http.Json;
using StoreShared.Sales;

namespace StorePOS.Services;

public sealed class RefundService
{
    private readonly HttpClient _http;

    public RefundService(HttpClient http) => _http = http;

    public async Task<SaleByReceiptDto?> GetSaleByReceiptAsync(string receiptNumber, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SaleByReceiptDto>(
            $"api/sales/receipt/{Uri.EscapeDataString(receiptNumber.Trim())}",
            AppJson.Options,
            ct);

    public async Task<RefundHistoryDto?> GetRefundHistoryAsync(string receiptNumber, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<RefundHistoryDto>(
            $"api/refunds/{Uri.EscapeDataString(receiptNumber.Trim())}",
            AppJson.Options,
            ct);

    public async Task<SaleReceiptHistoryDto?> GetReceiptHistoryAsync(string receiptNumber, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SaleReceiptHistoryDto>(
            $"api/sales/receipt/{Uri.EscapeDataString(receiptNumber.Trim())}/history",
            AppJson.Options,
            ct);

    public async Task<RefundResultDto?> ProcessRefundAsync(RefundRequestDto request, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/refunds", request, AppJson.Options, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadFromJsonAsync<RefundResultDto>(AppJson.Options, ct);
            return err ?? new RefundResultDto(false, await resp.Content.ReadAsStringAsync(ct), 0, "");
        }

        return await resp.Content.ReadFromJsonAsync<RefundResultDto>(AppJson.Options, ct);
    }

    public Task<SalesStatisticsDto?> GetStatisticsAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        _http.GetApiJsonAsync<SalesStatisticsDto>(
            $"api/sales/statistics?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            ct);

    public async Task<PagedSaleEventsResult?> GetEventsAsync(
        DateOnly from,
        DateOnly to,
        SaleEventType? type,
        string? performedBy,
        int page,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"api/sales/events?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&page={page}&pageSize={pageSize}";
        if (type is not null)
            url += $"&type={type.Value}";
        if (!string.IsNullOrWhiteSpace(performedBy))
            url += $"&performedBy={Uri.EscapeDataString(performedBy.Trim())}";
        return await _http.GetApiJsonAsync<PagedSaleEventsResult>(url, ct);
    }

    public async Task<(byte[]? Bytes, string? Error)> ExportEventsCsvAsync(
        DateOnly from,
        DateOnly to,
        SaleEventType? type,
        string? performedBy,
        CancellationToken ct = default)
    {
        var url = $"api/sales/events/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (type is not null)
            url += $"&type={type.Value}";
        if (!string.IsNullOrWhiteSpace(performedBy))
            url += $"&performedBy={Uri.EscapeDataString(performedBy.Trim())}";
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return (null, await resp.Content.ReadAsStringAsync(ct));
        return (await resp.Content.ReadAsByteArrayAsync(ct), null);
    }
}
