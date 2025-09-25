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
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Security.Principal;

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

// ==== MOVE dataRoot & dbPath UP HERE (before builder + AddDbContext) ====
var dataRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "QueueSystem");
Directory.CreateDirectory(dataRoot);
var dbPath = Path.Combine(dataRoot, "queue.db");
Log.Information("Using data directory {Dir}", dataRoot);
// =======================================================================

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ====== REGISTER SERVICES (dbPath now defined) ======
builder.Services.AddDbContext<QueueDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<QueueServer.Api.Utils.QueueService>(); // jika QueueService ada di Api.Utils
// ===========================================

// Naikkan batas body agar upload video besar tidak terputus
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 512L * 1024 * 1024; // 512 MB
});

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 512L * 1024 * 1024;
});

// (Optional) buat subfolder Resources untuk file default bila diperlukan
void CopySeed(string fileName)
{
    try
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        var dest = Path.Combine(dataRoot, "Resources", fileName);
        if (File.Exists(src) && !File.Exists(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Seed copy failed for {File}", fileName);
    }
}
CopySeed("logo.png");
CopySeed("info.mp4");
CopySeed("chime.wav");

// static files untuk melayani video dari ProgramData (bukan Program Files)
builder.Services.AddDirectoryBrowser();
builder.Services.AddSignalR(); // pastikan ada
builder.Services.AddControllers();

// Tambahkan ini:
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "QueueServer API", Version = "v1" });
});

// ====== TENTUKAN BINDING SEBELUM Build ======
string urls = ResolveUrls(args, dataRoot);
builder.WebHost.UseUrls(urls);
Log.Information("Binding to {Urls}", urls);
// ====== END BINDING ======

var app = builder.Build();

// Buka firewall (best effort) setelah app dibuat
TryOpenFirewall(urls);

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

// Map static files: wwwroot (default)
app.UseStaticFiles();

// Map static files: ProgramData\videos => /videos
var videosRoot = Path.Combine(dataRoot, "videos");
Directory.CreateDirectory(videosRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(videosRoot),
    RequestPath = "/videos",
    ServeUnknownFileTypes = true
});

// GET semua settings, PUT settings, upload, dll
app.MapGet("/api/settings", async ([FromServices] ISettingsService s) =>
    Results.Ok(await s.GetAllAsync()))
   .Produces<IDictionary<string,string>>(StatusCodes.Status200OK);

app.MapPut("/api/settings", async (
    Dictionary<string, string> updates,
    [FromServices] ISettingsService s,
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
        var feat = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feat != null) feat.MaxRequestBodySize = 512L * 1024 * 1024;

        if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data required");
        var form = await req.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return Results.BadRequest("no file");

        var videosDir = Path.Combine(dataRoot, "videos");
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

// ====== HELPER FUNCTIONS (letakkan di bawah) ======
static string ResolveUrls(string[] args, string dataRoot)
{
    var argUrls = args.FirstOrDefault(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
    if (argUrls != null)
        return argUrls.Split(' ', 2).Last().Trim('"');

    string fallback = "http://0.0.0.0:5000";
    string? envUrls = Environment.GetEnvironmentVariable("QUEUE_URLS");
    string? cfgUrls = null;
    try
    {
        var cfgPath = Path.Combine(dataRoot, "server.json");
        if (File.Exists(cfgPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
            if (doc.RootElement.TryGetProperty("Urls", out var v))
                cfgUrls = v.GetString();
        }
    }
    catch (Exception ex) { Log.Warning(ex, "Read server.json failed"); }
    return (cfgUrls ?? envUrls ?? fallback).Trim();
}

static void TryOpenFirewall(string urls)
{
    try
    {
        if (!OperatingSystem.IsWindows()) return;
        using var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            Log.Warning("Firewall rule not created (not elevated)");
            return;
        }
        var first = urls.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return;
        if (!Uri.TryCreate(first, UriKind.Absolute, out var u)) return;
        var port = u.Port;
        var name = $"QueueServer {port}";
        var show = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{name}\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        });
        var output = show!.StandardOutput.ReadToEnd();
        show.WaitForExit(3000);
        if (output.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Firewall rule exists: {Name}", name);
            return;
        }
        Process.Start(new ProcessStartInfo("netsh",
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port}")
        { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
        Log.Information("Firewall rule added for TCP {Port}", port);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Ensure firewall rule failed");
    }
}
// ====== END HELPERS ======

app.MapHub<QueueServer.Api.Hubs.QueueHub>("/hub/queue");  // <<< FIX 404 negotiate
// (Opsional jika pakai Controllers)
// app.MapControllers();

app.Run();