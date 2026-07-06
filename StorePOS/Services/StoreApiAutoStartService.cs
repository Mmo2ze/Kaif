using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using StoreShared;

namespace StorePOS.Services;

/// <summary>
/// Ensures the local StoreAPI process is reachable on 127.0.0.1:5050, optionally starting StoreAPI from the app folder (Windows + macOS).
/// </summary>
public sealed class StoreApiAutoStartService
{
    private static readonly Uri LocalApi = new("http://127.0.0.1:5050/");

    private static readonly JsonSerializerOptions HealthJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ApiExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "StoreAPI.exe" : "StoreAPI";

    /// <summary>
    /// Waits until /api/health succeeds with the expected label renderer or the deadline passes.
    /// </summary>
    public async Task<bool> EnsureApiAvailableAsync(Func<string?, Task>? statusAsync = null, CancellationToken cancellationToken = default)
    {
        if (statusAsync is not null)
            await statusAsync("Checking store server…").ConfigureAwait(false);

        using var client = CreateProbeClient();

        if (await IsExpectedApiHealthyAsync(client, cancellationToken).ConfigureAwait(false))
            return true;

        if (await IsAnyApiHealthyAsync(client, cancellationToken).ConfigureAwait(false))
        {
            if (statusAsync is not null)
                await statusAsync("Replacing outdated store server…").ConfigureAwait(false);

            StopStoreApiProcesses();
            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

            if (await IsExpectedApiHealthyAsync(client, cancellationToken).ConfigureAwait(false))
                return true;
        }

        if (statusAsync is not null)
            await statusAsync("Starting server…").ConfigureAwait(false);

        var launchNote = await TryLaunchStoreApiProcessAsync(cancellationToken).ConfigureAwait(false);
        if (launchNote is not null && statusAsync is not null)
            await statusAsync(launchNote).ConfigureAwait(false);

        var start = DateTime.UtcNow;
        var deadline = start.AddMinutes(6);
        var lastProgressUtc = DateTime.MinValue;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (await IsExpectedApiHealthyAsync(client, cancellationToken).ConfigureAwait(false))
                return true;

            if (statusAsync is not null && (DateTime.UtcNow - lastProgressUtc).TotalSeconds >= 4)
            {
                lastProgressUtc = DateTime.UtcNow;
                var elapsed = (int)(DateTime.UtcNow - start).TotalSeconds;
                var hint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "ensure StoreAPI.exe is beside StorePOS.exe"
                    : "ensure StoreAPI is inside Store POS.app/Contents/MacOS";
                await statusAsync($"Starting server… ({elapsed}s) — {hint}. If this never finishes, see StorePOS-api-launch.log.").ConfigureAwait(false);
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return await IsExpectedApiHealthyAsync(client, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateProbeClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
        };
        return new HttpClient(handler)
        {
            BaseAddress = LocalApi,
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<bool> IsAnyApiHealthyAsync(HttpClient client, CancellationToken ct) =>
        await FetchHealthAsync(client, ct).ConfigureAwait(false) is not null;

    private static async Task<bool> IsExpectedApiHealthyAsync(HttpClient client, CancellationToken ct)
    {
        var health = await FetchHealthAsync(client, ct).ConfigureAwait(false);
        return health is not null &&
               string.Equals(health.LabelRenderVersion, StoreBuild.LabelRenderVersion, StringComparison.Ordinal);
    }

    private static async Task<HealthProbe?> FetchHealthAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync("api/health", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<HealthProbe>(stream, HealthJson, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static void StopStoreApiProcesses()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (!proc.ProcessName.Equals("StoreAPI", StringComparison.OrdinalIgnoreCase))
                    continue;

                LogLaunch($"Stopping stale StoreAPI pid={proc.Id}");
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                LogLaunch($"Stop StoreAPI failed pid={proc.Id}: {ex.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static async Task<string?> TryLaunchStoreApiProcessAsync(CancellationToken cancellationToken)
    {
        var path = ResolveStoreApiPath();
        if (path is null || !File.Exists(path))
        {
            var expect = Path.Combine(GetAppDirectory(), ApiExecutableName);
            LogLaunch($"{ApiExecutableName} not found. Expected: {expect}");
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"StoreAPI.exe not found. Use scripts\\publish-windows.ps1 and run from the publish folder. Log: StorePOS-api-launch.log"
                : $"StoreAPI not found. Use scripts/publish-macos.sh and open Store POS.app from the publish folder. Log: StorePOS-api-launch.log";
        }

        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? ".",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            LogLaunch($"Process.Start failed: {ex}");
            return $"Could not start StoreAPI: {ex.Message}. See StorePOS-api-launch.log.";
        }

        if (p is null)
        {
            LogLaunch("Process.Start returned null.");
            return "Could not start StoreAPI (Process.Start returned null). See StorePOS-api-launch.log";
        }

        LogLaunch($"Started process Id={p.Id} Path={path}");

        try
        {
            await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
            p.Refresh();
            if (p.HasExited)
            {
                LogLaunch($"StoreAPI exited within 3s ExitCode={p.ExitCode}");
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? $"StoreAPI quit right after start (exit {p.ExitCode}). Run publish-windows.ps1 and start from the full publish folder."
                    : $"StoreAPI quit right after start (exit {p.ExitCode}). Run publish-macos.sh and open the published Store POS.app.";
            }
        }
        finally
        {
            p.Dispose();
        }

        return null;
    }

    private static string GetAppDirectory()
    {
        var processDir = Path.GetDirectoryName(Environment.ProcessPath);
        return string.IsNullOrEmpty(processDir) ? AppContext.BaseDirectory : processDir;
    }

    private static string? ResolveStoreApiPath()
    {
        var name = ApiExecutableName;
        string?[] candidates =
        [
            Path.Combine(GetAppDirectory(), name),
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name),
        ];

        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(c))
                return c;
        }

        return null;
    }

    private static void LogLaunch(string line)
    {
        try
        {
            var logPath = Path.Combine(GetAppDirectory(), "StorePOS-api-launch.log");
            File.AppendAllText(logPath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed record HealthProbe(string? LabelRenderVersion, int? ApiVersion, string[]? Features);
}
