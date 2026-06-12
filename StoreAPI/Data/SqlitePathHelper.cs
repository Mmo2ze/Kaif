using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace StoreAPI.Data;

public static class SqlitePathHelper
{
    /// <summary>Full path to the SQLite database file (from DefaultConnection).</summary>
    public static string ResolveDatabaseFilePath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var raw = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=store.db";
        const string prefix = "Data Source=";
        if (!raw.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DefaultConnection must be a SQLite Data Source= connection string.");
        var relative = raw.AsSpan(prefix.Length).Trim().ToString();
        if (Path.IsPathRooted(relative))
            return relative;
        return Path.Combine(environment.ContentRootPath, relative);
    }

    public static string ResolveSqliteConnectionString(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var raw = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=store.db";
        const string prefix = "Data Source=";
        if (!raw.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return raw;
        var relative = raw.AsSpan(prefix.Length).Trim().ToString();
        if (Path.IsPathRooted(relative))
            return raw;
        var full = Path.Combine(environment.ContentRootPath, relative);
        return $"{prefix}{full}";
    }
}
