using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc; // for [FromServices]
using Microsoft.EntityFrameworkCore;
using QueueServer.Core.Data;
using QueueServer.Core.Services;
using QueueServer.Api.Hubs;
using QueueServer.Api.Utils;
using Serilog;
using Serilog.Extensions.Hosting;
using QueueServer.Api;
using System.Text.Json.Serialization; // Tambahan untuk JsonStringEnumConverter
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Shared.Contracts;
using QueueServer.Core.Models;

// Bootstrap Serilog sebelum membuat builder
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "Logs", "server-.log"),
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 7,
                  shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(); // integrasikan Serilog dengan host

// Registrasi logger & DiagnosticContext ke DI (untuk RequestLoggingMiddleware)
builder.Services.AddSingleton<Serilog.ILogger>(Log.Logger);
builder.Services.AddSingleton<DiagnosticContext>(sp =>
    new DiagnosticContext(sp.GetRequiredService<Serilog.ILogger>()));

// Naikkan batas body agar upload video besar tidak terputus
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 512L * 1024 * 1024; // 512 MB
});

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 512L * 1024 * 1024;
});

// Lokasi DB
var dataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "queue.db");

builder.Services.AddDbContext<QueueDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// static files untuk melayani video dari wwwroot
builder.Services.AddDirectoryBrowser();
builder.Services.AddSignalR(); // pastikan ada
builder.Services.AddControllers();

// Tambahkan ini:
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "QueueServer API", Version = "v1" });
});

// batasi upload, bisa disesuaikan
builder.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = 512L * 1024 * 1024; });

// Registrasi services
builder.Services.AddScoped<QueueServer.Core.Services.ITicketService, QueueServer.Core.Services.TicketService>();
builder.Services.AddScoped<QueueServer.Core.Services.ISettingsService, QueueServer.Core.Services.SettingsService>();
builder.Services.AddScoped<QueueServer.Api.Utils.QueueService>();

// KONFIG: Enum -> String
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // (Opsional) o.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

// Tetap boleh pakai
app.UseSerilogRequestLogging(); // log request

// Jalankan migrasi + seed, tapi aman jika gagal
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<QueueDbContext>();
        db.Database.Migrate(); // AKTIFKAN KEMBALI
        var settings = scope.ServiceProvider.GetRequiredService<QueueServer.Core.Services.ISettingsService>();
        await settings.EnsureDefaultsAsync();
        Log.Information("DB initialized");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "DB init failed (migrate/seed)");
        // biarkan server tetap hidup agar /api/debug/health bisa dicek
    }
}

app.UseStaticFiles();
app.MapControllers();
app.MapHub<QueueHub>("/hub/queue");

// Health & Ping
app.MapGet("/api/debug/ping", () => Results.Ok(new { ok = true, time = DateTime.UtcNow }))
   .Produces(StatusCodes.Status200OK);

app.MapGet("/api/debug/health", async ([FromServices] QueueDbContext db) =>
{
    try
    {
        var canQuery = await db.Database.CanConnectAsync();
        var settingsCount = await db.Settings.CountAsync();
        return Results.Ok(new { ok = canQuery, settingsCount });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Health check failed");
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// + GET semua settings (dipakai Admin/Display)
app.MapGet("/api/settings", async ([FromServices] QueueServer.Core.Services.ISettingsService s) =>
    Results.Ok(await s.GetAllAsync()))
   .Produces<IDictionary<string,string>>(StatusCodes.Status200OK);

// PUT settings (sudah ada)
app.MapPut("/api/settings", async (
    Dictionary<string, string> updates,
    QueueServer.Core.Services.ISettingsService s,
    [FromServices] IHubContext<QueueHub> hub,
    ILogger<Program> logger) =>
{
    foreach (var kv in updates)
        await s.SetValueAsync(kv.Key, kv.Value);

    await hub.Clients.All.SendAsync("SettingsChanged");
    return Results.Ok();
});

// upload video
app.MapPost("/api/media/upload", async (
    HttpRequest req,
    QueueServer.Core.Services.ISettingsService settings,
    [FromServices] IHubContext<QueueHub> hub) =>
{
    try
    {
        // pastikan per-request limit cukup besar
        var feat = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feat != null) feat.MaxRequestBodySize = 512L * 1024 * 1024;

        if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data required");
        var form = await req.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return Results.BadRequest("no file");

        var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var videosDir = Path.Combine(webRoot, "videos");
        Directory.CreateDirectory(videosDir);

        var safeName = Path.GetFileNameWithoutExtension(file.FileName);
        var ext = Path.GetExtension(file.FileName);
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeName}{ext}";
        var outPath = Path.Combine(videosDir, name);

        await using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await file.CopyToAsync(fs);
        }

        var url = $"/videos/{name}";
        await settings.SetValueAsync("VideoPath", url);
        await settings.SetValueAsync("ShowVideo", "true");

        await hub.Clients.All.SendAsync(Shared.Contracts.HubEvents.SettingsChanged);
        return Results.Ok(new { url });
    }
    catch (Exception ex)
    {
        Serilog.Log.Error(ex, "Upload failed");
        return Results.Problem(ex.Message, statusCode: 500);
    }
})
.Accepts<IFormFile>("multipart/form-data")
.Produces(StatusCodes.Status200OK);

// reset nomor
app.MapPost("/api/admin/reset", async (
    QueueServer.Api.Utils.QueueService svc,
    [FromServices] IHubContext<QueueHub> hub) =>
{
    await svc.ResetAsync();
    await hub.Clients.All.SendAsync("QueueReset");
    return Results.Ok();
})
.Produces(StatusCodes.Status200OK);

// Aktifkan Swagger (boleh hanya saat Development)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "QueueServer API v1");
    c.RoutePrefix = "swagger";
});

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("QueueServer.Api started at {Time}", DateTime.Now));

app.MapPost("/api/queue/next/{counter:int}", async (
    int counter,
    QueueServer.Core.Services.ITicketService svc,
    IHubContext<QueueHub> hub) =>
{
    var t = await svc.CallNextAsync(counter);
    if (t is null) return Results.NotFound();

    // Broadcast ke semua klien
    await hub.Clients.All.SendAsync(HubEvents.TicketCalled, new
    {
        t.Id,
        t.TicketNumber,
        t.CounterNumber
    });

    // Kembalikan ke Teller
    return Results.Ok(new
    {
        t.Id,
        t.TicketNumber,
        Status = t.Status.ToString(),
        t.CounterNumber
    });
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// Daftar tiket hari ini (dipakai Display untuk "Sedang Dilayani")
app.MapGet("/api/tickets/today", async ([FromServices] QueueDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    var list = await db.Tickets
        .Where(t => t.Date == today)
        .OrderByDescending(t => t.CalledAt ?? DateTime.MinValue)
        .ThenByDescending(t => t.Id)
        .Select(t => new {
            t.Id,
            t.TicketNumber,
            Status = t.Status.ToString(),
            t.CounterNumber,
            t.CalledAt
        })
        .ToListAsync();

    return Results.Ok(list);
})
.Produces(StatusCodes.Status200OK);

app.Run();