using Microsoft.EntityFrameworkCore;
using QueueServer.Core.Data;
using QueueServer.Core.Services;
using QueueServer.Api.Hubs;
using QueueServer.Api.Utils;
using Serilog;
using QueueServer.Api;
using System.Text.Json.Serialization; // Tambahan untuk JsonStringEnumConverter

var builder = WebApplication.CreateBuilder(args);
builder.AddSerilogLogging();

// Lokasi DB
var dataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "queue.db");

builder.Services.AddDbContext<QueueDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSignalR();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddSingleton<HubBroadcaster>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// KONFIG: Enum -> String
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // (Opsional) o.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QueueDbContext>();
    db.Database.Migrate();
    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
    await settings.EnsureDefaultsAsync();
}

app.MapHub<QueueHub>("/hub/queue");

// Endpoint debug (untuk verifikasi cepat)
app.MapGet("/api/debug/ping", () => Results.Ok(new { status = "ok" }));

// Endpoint tiket & queue
app.MapPost("/api/tickets", async (ITicketService svc, HubBroadcaster hub) =>
{
    var ticket = await svc.CreateTicketAsync();
    await hub.TicketCreated(new { ticket.Id, ticket.TicketNumber });
    return Results.Ok(ticket);
});

app.MapPost("/api/queue/next/{counter:int}", async (int counter, ITicketService svc, HubBroadcaster hub, ILogger<Program> logger) =>
{
    logger.LogInformation("CallNext requested for counter {Counter}", counter);
    var t = await svc.CallNextAsync(counter);
    if (t == null) return Results.NotFound();
    await hub.TicketCalled(new { t.Id, t.TicketNumber, t.CounterNumber });
    return Results.Ok(t);
});

app.MapPost("/api/tickets/{id:int}/recall", async (int id, ITicketService svc, HubBroadcaster hub) =>
{
    var t = await svc.RecallAsync(id);
    if (t == null) return Results.NotFound();
    await hub.TicketCalled(new { t.Id, t.TicketNumber, t.CounterNumber, Recall = true });
    return Results.Ok(t);
});

app.MapPost("/api/tickets/{id:int}/serveStart", async (int id, ITicketService svc, HubBroadcaster hub) =>
{
    var t = await svc.StartServingAsync(id);
    if (t == null) return Results.NotFound();
    await hub.TicketUpdated(new { t.Id, t.Status });
    return Results.Ok(t);
});

app.MapPost("/api/tickets/{id:int}/complete", async (int id, ITicketService svc, HubBroadcaster hub) =>
{
    var t = await svc.CompleteAsync(id);
    if (t == null) return Results.NotFound();
    await hub.TicketUpdated(new { t.Id, t.Status });
    return Results.Ok(t);
});

app.MapPost("/api/tickets/{id:int}/skip", async (int id, ITicketService svc, HubBroadcaster hub) =>
{
    var t = await svc.SkipAsync(id);
    if (t == null) return Results.NotFound();
    await hub.TicketUpdated(new { t.Id, t.Status });
    return Results.Ok(t);
});

app.MapGet("/api/tickets/today", async (ITicketService svc) =>
{
    var list = await svc.GetTodayTicketsAsync();
    return Results.Ok(list);
});

app.MapGet("/api/settings", async (ISettingsService s) =>
{
    var all = await s.GetAllAsync();
    return Results.Ok(all);
});

app.MapPut("/api/settings", async (Dictionary<string,string> updates, ISettingsService s, HubBroadcaster hub, ILogger<Program> logger) =>
{
    foreach (var kv in updates)
    {
        await s.SetValueAsync(kv.Key, kv.Value);
        logger.LogInformation("Setting updated {Key}={Value}", kv.Key, kv.Value);
    }
    await hub.SettingsChanged(updates);
    return Results.Ok();
});

app.UseSwagger();
app.UseSwaggerUI();

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("QueueServer.Api started at {Time}", DateTime.Now));

app.Run();