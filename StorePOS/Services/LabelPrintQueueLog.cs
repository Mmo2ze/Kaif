namespace StorePOS.Services;

internal static class LabelPrintQueueLog
{
    private static readonly object Gate = new();
    private static string? _path;

    private static string LogPath
    {
        get
        {
            if (_path is not null)
                return _path;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Store POS");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "label-print-queue.log");
            return _path;
        }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (Gate)
        {
            File.AppendAllText(LogPath, line);
        }
    }
}
