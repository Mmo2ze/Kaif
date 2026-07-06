using System.Net.Http.Headers;
using System.Net.Http.Json;
using StoreShared.Catalog;
using StoreShared.Print;

namespace StorePOS.Services;

/// <summary>Polls the API print queue and sends jobs to the local barcode printer.</summary>
public sealed class LabelPrintQueueWorker : IDisposable
{
    private static readonly Uri ApiBase = new("http://127.0.0.1:5050/");
    private readonly AppState _app;
    private readonly IBarcodePrintService _print;
    private readonly Timer _timer;
    private int _busy;

    public LabelPrintQueueWorker(AppState app, IBarcodePrintService print)
    {
        _app = app;
        _print = print;
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _app.Changed += OnAppChanged;
    }

    public void Start()
    {
        if (!_print.IsSupported)
        {
            LabelPrintQueueLog.Write("Start skipped — barcode print not supported on this platform.");
            return;
        }

        LabelPrintQueueLog.Write(_app.IsAuthenticated
            ? $"Worker started (poll every 2s). POS user={_app.Username}."
            : "Worker started (poll every 2s). Waiting for Store POS login — web login alone is not enough.");
        _timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void OnAppChanged()
    {
        if (_app.IsAuthenticated && _print.IsSupported && !string.IsNullOrWhiteSpace(_print.GetSelectedPrinter()))
            Start();
    }

    private async Task PollAsync()
    {
        if (!_app.IsAuthenticated)
            return;

        if (!_print.IsSupported)
            return;

        var printer = _print.GetSelectedPrinter();
        if (string.IsNullOrWhiteSpace(printer))
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        try
        {
            using var client = CreateClient();
            using var resp = await client.GetAsync("api/print/labels/next");
            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                return;

            if (!resp.IsSuccessStatusCode)
            {
                LabelPrintQueueLog.Write($"Poll GET next failed HTTP {(int)resp.StatusCode}.");
                return;
            }

            var job = await resp.Content.ReadFromJsonAsync<LabelPrintJobDto>(AppJson.Options);
            if (job is null || string.IsNullOrWhiteSpace(job.Barcode))
            {
                LabelPrintQueueLog.Write("Poll got empty job payload.");
                return;
            }

            LabelPrintQueueLog.Write($"Processing job {job.Id} barcode {job.Barcode} count {job.Count} printer={printer}.");

            var key = Uri.EscapeDataString(job.Barcode);
            var detail = await client.GetFromJsonAsync<SkuDetailDto>(
                $"api/skus/{key}?forLabelPrint=true",
                AppJson.Options);
            if (detail is null || string.IsNullOrEmpty(detail.BarcodePngBase64))
            {
                LabelPrintQueueLog.Write($"Job {job.Id} — SKU/label image missing for barcode {job.Barcode}.");
                return;
            }

            var printed = await _print.PrintPngBase64Async(detail.BarcodePngBase64, job.Count);
            if (!printed)
            {
                LabelPrintQueueLog.Write($"Job {job.Id} — native print returned false.");
                return;
            }

            using var ack = await client.PostAsync($"api/print/labels/{job.Id}/ack", null);
            if (!ack.IsSuccessStatusCode)
            {
                LabelPrintQueueLog.Write($"Job {job.Id} printed but ack failed HTTP {(int)ack.StatusCode}.");
                return;
            }

            LabelPrintQueueLog.Write($"Job {job.Id} printed and acked.");
        }
        catch (Exception ex)
        {
            LabelPrintQueueLog.Write($"Poll error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = ApiBase, Timeout = TimeSpan.FromSeconds(30) };
        if (_app.Token is { Length: > 0 } t)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
        return client;
    }

    public void Dispose()
    {
        _app.Changed -= OnAppChanged;
        _timer.Dispose();
    }
}
