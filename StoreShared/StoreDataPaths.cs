namespace StoreShared;

/// <summary>Writable app data outside the install bundle (Mac .app / Windows publish folder).</summary>
public static class StoreDataPaths
{
    private const string AppFolderName = "Store POS";
    private const string DatabaseFileName = "store.db";

    public static string ResolveDataDirectory(string apiAssemblyDir)
    {
        if (!ShouldUsePersistentDataDirectory(apiAssemblyDir))
            return apiAssemblyDir;

        var dir = Path.Combine(GetPersistentRoot(), AppFolderName);
        Directory.CreateDirectory(dir);
        TryMigrateLegacyDatabase(apiAssemblyDir, dir);
        return dir;
    }

    public static string ResolveDatabasePath(string apiAssemblyDir)
    {
        var dbPath = Path.Combine(ResolveDataDirectory(apiAssemblyDir), DatabaseFileName);
        RemoveCorruptDatabaseIfNeeded(dbPath);
        return dbPath;
    }

    public static bool IsValidSqliteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            if (new FileInfo(path).Length < 100)
                return false;

            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            if (fs.Read(header) < 16)
                return false;

            return header[0] == (byte)'S'
                   && header[1] == (byte)'Q'
                   && header[2] == (byte)'L'
                   && header[3] == (byte)'i';
        }
        catch
        {
            return false;
        }
    }

    public static void RemoveCorruptDatabaseIfNeeded(string dbPath)
    {
        if (!File.Exists(dbPath))
            return;

        if (IsValidSqliteFile(dbPath))
            return;

        TryDeleteFile(dbPath);
        TryDeleteFile(dbPath + "-wal");
        TryDeleteFile(dbPath + "-shm");
        TryDeleteFile(dbPath + "-journal");
    }

    private static bool ShouldUsePersistentDataDirectory(string apiAssemblyDir)
    {
        if (OperatingSystem.IsMacOS())
            return apiAssemblyDir.Contains(".app/Contents/MacOS", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            var posExe = Path.Combine(apiAssemblyDir, "StorePOS.exe");
            if (File.Exists(posExe))
                return true;
        }

        return false;
    }

    private static string GetPersistentRoot()
    {
        if (OperatingSystem.IsMacOS())
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static void TryMigrateLegacyDatabase(string legacyDir, string dataDir)
    {
        var legacyDb = Path.Combine(legacyDir, DatabaseFileName);
        var targetDb = Path.Combine(dataDir, DatabaseFileName);
        if (File.Exists(targetDb) || !IsValidSqliteFile(legacyDb))
            return;

        try
        {
            File.Copy(legacyDb, targetDb);
        }
        catch
        {
            /* best effort */
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* best effort */
        }
    }
}
