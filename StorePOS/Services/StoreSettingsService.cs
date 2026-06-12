using System.Net.Http.Json;
using StoreShared.Pos;

namespace StorePOS.Services;

/// <summary>Store branding and POS options, loaded from the API (singleton).</summary>
public sealed class StoreSettingsService
{
    public string StoreName { get; private set; } = StoreBranding.StoreName;

    public string CurrencyLabel { get; private set; } = "EGP";

    public bool AllowSellerDiscount { get; private set; }

    public int LowStockThreshold { get; private set; } = 5;

    public string ReceiptLandline { get; private set; } = "";

    public string ReceiptPhone { get; private set; } = "";

    public event Action? Changed;

    public void Apply(PosSettingsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.StoreName))
            StoreName = dto.StoreName;
        if (!string.IsNullOrWhiteSpace(dto.CurrencyLabel))
            CurrencyLabel = dto.CurrencyLabel;
        AllowSellerDiscount = dto.AllowSellerDiscount;
        if (dto.LowStockThreshold >= 0)
            LowStockThreshold = dto.LowStockThreshold;
        ReceiptLandline = dto.ReceiptLandline?.Trim() ?? "";
        ReceiptPhone = dto.ReceiptPhone?.Trim() ?? "";
        Changed?.Invoke();
    }

    public async Task LoadFromApiAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await http.GetFromJsonAsync<PosSettingsDto>("api/settings", AppJson.Options, cancellationToken);
            if (dto is not null)
                Apply(dto);
        }
        catch
        {
            // Keep defaults / last snapshot if the server is unreachable.
        }
    }
}
