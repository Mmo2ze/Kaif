namespace StorePOS.Services;

/// <summary>Stores the receipt logo image on this PC (printed at the top of thermal receipts).</summary>
public sealed class ReceiptLogoStore
{
    public string LogoPath { get; } = Path.Combine(FileSystem.AppDataDirectory, "receipt-logo.png");

    public bool HasLogo => File.Exists(LogoPath);

    /// <summary>Last write ticks, used to invalidate cached raster data after a new upload.</summary>
    public long Version => HasLogo ? File.GetLastWriteTimeUtc(LogoPath).Ticks : 0;

    public async Task SaveAsync(Stream source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogoPath)!);
        await using var file = File.Create(LogoPath);
        await source.CopyToAsync(file);
    }

    public void Remove()
    {
        if (HasLogo)
            File.Delete(LogoPath);
    }

    public string? GetBase64()
    {
        if (!HasLogo)
            return null;
        try
        {
            return Convert.ToBase64String(File.ReadAllBytes(LogoPath));
        }
        catch
        {
            return null;
        }
    }
}
