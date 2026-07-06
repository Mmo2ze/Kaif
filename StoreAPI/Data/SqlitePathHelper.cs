using Microsoft.Extensions.Configuration;

namespace StoreAPI.Data;

public static class SqlitePathHelper
{
    /// <summary>Full path to the SQLite database file.</summary>
    public static string ResolveDatabaseFilePath(IConfiguration configuration, string apiAssemblyDir)
    {
        var configured = ReadConfiguredRelativePath(configuration);
        if (Path.IsPathRooted(configured))
        {
            StoreShared.StoreDataPaths.RemoveCorruptDatabaseIfNeeded(configured);
            return configured;
        }

        return StoreShared.StoreDataPaths.ResolveDatabasePath(apiAssemblyDir);
    }

    public static string ResolveSqliteConnectionString(IConfiguration configuration, string apiAssemblyDir)
    {
        var dbPath = ResolveDatabaseFilePath(configuration, apiAssemblyDir);
        return $"Data Source={dbPath}";
    }

    private static string ReadConfiguredRelativePath(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=store.db";
        const string prefix = "Data Source=";
        if (!raw.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DefaultConnection must be a SQLite Data Source= connection string.");
        return raw.AsSpan(prefix.Length).Trim().ToString();
    }
}
