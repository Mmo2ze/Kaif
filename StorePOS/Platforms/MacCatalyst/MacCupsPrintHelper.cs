using System.Diagnostics;
using System.Text;

namespace StorePOS.Platforms.MacCatalyst;

internal static class MacCupsPrintHelper
{
    private const string LpPath = "/usr/bin/lp";
    private const string LpstatPath = "/usr/bin/lpstat";
    private const string LpoptionsPath = "/usr/bin/lpoptions";

    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        if (Run(LpstatPath, ["-a"], out var stdout, out _, captureOutput: true) != 0)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ');
            if (space <= 0)
                continue;

            var name = line[..space].Trim();
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(name);
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static bool PrinterExists(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return false;

        return GetInstalledPrinters().Contains(printerName.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static string? GetPaperSummary(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return null;

        if (Run(LpoptionsPath, ["-p", printerName], out var stdout, out _, captureOutput: true) != 0)
            return null;

        foreach (var token in stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!token.StartsWith("PageSize=", StringComparison.Ordinal))
                continue;

            return DescribePageSize(token["PageSize=".Length..]);
        }

        return null;
    }

    /// <summary>ESC/POS bytes for receipt printers that accept CUPS raw jobs.</summary>
    public static bool PrintRawBytes(string printerName, byte[] data) =>
        !string.IsNullOrWhiteSpace(printerName) && data.Length > 0 && RunLpRaw(printerName, data);

    /// <summary>Label bitmap via the CUPS image filter (matches Windows GDI bitmap printing).</summary>
    public static bool PrintImageFile(string printerName, string filePath, int widthMm, int heightMm, int copies = 1)
    {
        if (string.IsNullOrWhiteSpace(printerName) || !File.Exists(filePath))
            return false;

        var pageSize = $"Custom.{widthMm}x{heightMm}mm";
        var copyCount = Math.Clamp(copies, 1, 500).ToString();

        // Try the full option set first, then simpler fallbacks — some drivers reject unknown keys.
        var attempts = new List<string[]>
        {
            new[] { "-d", printerName, "-n", copyCount, "-o", $"PageSize={pageSize}", "-o", "Resolution=203dpi",
                "-o", "fit-to-page=false", "-o", "scaling=100", "-o", "job-sheets=none", filePath },
            new[] { "-d", printerName, "-n", copyCount, "-o", $"PageSize={pageSize}", "-o", "Resolution=203dpi",
                "-o", "job-sheets=none", filePath },
            new[] { "-d", printerName, "-n", copyCount, "-o", $"PageSize={pageSize}", "-o", "job-sheets=none", filePath },
            new[] { "-d", printerName, "-n", copyCount, "-o", "job-sheets=none", filePath },
        };

        foreach (var args in attempts)
        {
            if (Run(LpPath, args, out _, out var stderr, captureOutput: true) == 0)
                return true;

            if (!string.IsNullOrWhiteSpace(stderr))
                LogPrint($"lp image failed ({string.Join(" ", args)}): {stderr.Trim()}");
        }

        return false;
    }

    private static bool RunLpRaw(string printerName, byte[] data)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"storepos-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(temp, data);
            var ok = Run(LpPath, ["-d", printerName, "-o", "raw", temp], out _, out var stderr, captureOutput: true) == 0;
            if (!ok && !string.IsNullOrWhiteSpace(stderr))
                LogPrint($"lp raw failed: {stderr.Trim()}");
            return ok;
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private static string DescribePageSize(string value)
    {
        if (value.StartsWith("Custom.", StringComparison.OrdinalIgnoreCase))
        {
            var dims = value["Custom.".Length..];
            return dims.Contains('x', StringComparison.Ordinal) ? $"{dims} (custom)" : value;
        }

        if (value.Length >= 3 && value[0] == 'w' && value.Contains('h'))
        {
            var hIdx = value.IndexOf('h');
            if (hIdx > 1 && int.TryParse(value[1..hIdx], out var wIn) && int.TryParse(value[(hIdx + 1)..], out var hIn))
                return $"{wIn}×{hIn} in ({wIn * 25.4:0.#}×{hIn * 25.4:0.#} mm)";
        }

        return value;
    }

    internal static void LogPrint(string message)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "label-debug");
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "print.log"), line, Encoding.UTF8);
        }
        catch
        {
            // best effort
        }
    }

    private static int Run(string fileName, IList<string> args, out string stdout, out string stderr, bool captureOutput)
    {
        stdout = "";
        stderr = "";
        try
        {
            if (!File.Exists(fileName))
            {
                LogPrint($"command not found: {fileName}");
                return -1;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = captureOutput,
                    RedirectStandardError = captureOutput,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            process.Start();
            if (captureOutput)
            {
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
            }

            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                LogPrint($"timed out: {fileName} {string.Join(" ", args)}");
                return -1;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            LogPrint($"{fileName} failed: {ex.Message}");
            return -1;
        }
    }
}
