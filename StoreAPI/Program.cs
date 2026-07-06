using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StoreAPI.Cors;
using StoreAPI.Data;
using StoreAPI.Options;
using StoreAPI.Services;
using StoreShared;

// Folder containing StoreAPI.dll (same as StoreAPI.exe in publish). Avoids broken startup when cwd is not the publish folder (Explorer, shortcuts, start without /D).
var apiAssemblyDir = Assembly.GetExecutingAssembly().Location is { Length: > 0 } loc
    ? Path.GetDirectoryName(loc)!
    : AppContext.BaseDirectory;
// Next to StorePOS, LAN SPA lives in browserwww/ (MAUI keeps wwwroot/ for Blazor WebView). Web root must be set in WebApplicationOptions — UseWebRoot after CreateBuilder(WebApplicationOptions) throws NotSupportedException.
var browserWwwRoot = Path.Combine(apiAssemblyDir, "browserwww");
var webRootPath = Directory.Exists(browserWwwRoot)
    ? browserWwwRoot
    : Path.Combine(apiAssemblyDir, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = apiAssemblyDir,
    WebRootPath = webRootPath,
});

builder.Host.UseWindowsService();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PosOptions>(builder.Configuration.GetSection(PosOptions.SectionName));
builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection("BackupSettings"));
builder.Services.AddMemoryCache();
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                 ?? new JwtOptions();

var sqliteConnection = SqlitePathHelper.ResolveSqliteConnectionString(builder.Configuration, apiAssemblyDir);

builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddSingleton<StoreRuntimeSettings>();
builder.Services.AddSingleton<SkuBarcodeImageService>();
builder.Services.AddSingleton<LabelPrintQueueService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<RefundService>();
builder.Services.AddScoped<SalesAnalyticsService>();
builder.Services.AddScoped<SkuLookupService>();
builder.Services.AddScoped<ICatalogImportService, CatalogImportService>();

builder.Services.AddHttpClient(DatabaseBackupService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddSingleton<IBackupRunner>(sp => sp.GetRequiredService<DatabaseBackupService>());
builder.Services.AddSingleton<IDatabaseRestoreService, DatabaseRestoreService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseBackupService>());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                if (HttpMethods.IsOptions(context.Request.Method))
                    context.HandleResponse();
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseSqlite(sqliteConnection));

builder.Services.AddLocalNetworkCors(builder.Environment);

WebApplication app;
try
{
    app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseStaticFiles(SpaStaticFileOptions.Create());

    app.UseRouting();
    app.UsePrivateNetworkAccessCors();
    app.UseCors(LocalNetworkCors.PolicyName);
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.MapGet("/api/health", () => Results.Ok(new
    {
        status = "ok",
        time = DateTimeOffset.UtcNow,
        apiVersion = StoreBuild.ApiVersion,
        labelRenderVersion = StoreBuild.LabelRenderVersion,
        features = new[] { "sales-statistics", "sales-events", "backup-v2", "react", "label-print-queue" },
    }))
        .WithName("Health");

    app.MapFallbackToFile("index.html", SpaStaticFileOptions.Create());

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        await DatabaseSchemaInitializer.ApplyAsync(db);
        scope.ServiceProvider.GetRequiredService<SkuBarcodeImageService>().ClearCache();
        await DatabaseSeeder.SeedAsync(db);
        var runtimeSettings = scope.ServiceProvider.GetRequiredService<StoreRuntimeSettings>();
        await runtimeSettings.RefreshAsync();
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    try
    {
        File.AppendAllText(
            Path.Combine(apiAssemblyDir, "StoreAPI-startup-error.txt"),
            $"{DateTime.UtcNow:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        /* ignore */
    }

    throw;
}
