using Microsoft.EntityFrameworkCore;
using QueueServer.Core.Data;
using QueueServer.Core.Models;

namespace QueueServer.Core.Services;

public class SettingsService : ISettingsService
{
    private readonly QueueDbContext _db;
    private static readonly Dictionary<string,string> DefaultSettings = new()
    {
        ["Prefix"] = "A",
        ["RunningText"] = "Selamat datang.",
        ["LogoPath"] = "Resources\\logo.png",
        ["VideoPath"] = "Resources\\info.mp4",
        ["ShowLogo"] = "true",
        ["ShowVideo"] = "true",
        ["ResetTime"] = "00:00",
        ["ChimePath"] = "Resources\\chime.wav"
    };

    public SettingsService(QueueDbContext db) => _db = db;

    public async Task EnsureDefaultsAsync()
    {
        foreach (var kv in DefaultSettings)
        {
            if (!await _db.Settings.AnyAsync(s => s.Key == kv.Key))
                _db.Settings.Add(new Setting { Key = kv.Key, Value = kv.Value });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string?> GetValueAsync(string key)
    {
        var s = await _db.Settings.FindAsync(key);
        return s?.Value;
    }

    public async Task SetValueAsync(string key, string value)
    {
        var s = await _db.Settings.FindAsync(key);
        if (s == null)
            _db.Settings.Add(new Setting { Key = key, Value = value });
        else
            s.Value = value;

        await _db.SaveChangesAsync();
    }

    public async Task<IDictionary<string,string>> GetAllAsync()
        => await _db.Settings.ToDictionaryAsync(x => x.Key, x => x.Value);
}