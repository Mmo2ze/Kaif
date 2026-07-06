using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using StorePOS.Services;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Windowing;
#endif

namespace StorePOS;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows =>
            {
                windows.OnWindowCreated(window =>
                {
                    window.ExtendsContentIntoTitleBar = false;
                    try
                    {
                        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                        var appWindow = AppWindow.GetFromWindowId(windowId);
                        if (appWindow.Presenter is OverlappedPresenter presenter)
                            presenter.Maximize();
                    }
                    catch
                    {
                        // Leave default window chrome if WinUI APIs are unavailable.
                    }
                });
            });
        });
#endif

        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<StoreApiAutoStartService>();
        builder.Services.AddSingleton<StoreSettingsService>();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<ReceiptLogoStore>();
#if WINDOWS
        builder.Services.AddSingleton<IBarcodePrintService, Platforms.Windows.WindowsBarcodePrintService>();
        builder.Services.AddSingleton<IReceiptPrintService, Platforms.Windows.WindowsReceiptPrintService>();
#elif MACCATALYST
        builder.Services.AddSingleton<IBarcodePrintService, Platforms.MacCatalyst.MacBarcodePrintService>();
        builder.Services.AddSingleton<IReceiptPrintService, Platforms.MacCatalyst.MacReceiptPrintService>();
#else
        builder.Services.AddSingleton<IBarcodePrintService, UnsupportedBarcodePrintService>();
        builder.Services.AddSingleton<IReceiptPrintService, UnsupportedReceiptPrintService>();
#endif
        builder.Services.AddSingleton<LabelPrintQueueWorker>();
        builder.Services.AddScoped<BarcodePrintHelper>();
        builder.Services.AddScoped<AuthHttpMessageHandler>(sp =>
            new AuthHttpMessageHandler(sp.GetRequiredService<AppState>())
            {
                // Avoid localhost (IPv6/DNS stalls on some PCs); match StoreApiAutoStartService.
                InnerHandler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                },
            });
        builder.Services.AddScoped(sp =>
        {
            var handler = sp.GetRequiredService<AuthHttpMessageHandler>();
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://127.0.0.1:5050/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
        });
        builder.Services.AddScoped<ProductService>();
        builder.Services.AddScoped<SaleService>();
        builder.Services.AddScoped<RefundService>();

        return builder.Build();
    }
}
