namespace QueueServer.Core.Services;

/// <summary>
/// Abstraksi penyimpanan settings ke DB.
/// </summary>
public interface ISettingsService
{
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value);
    Task<IDictionary<string,string>> GetAllAsync();
    Task EnsureDefaultsAsync();
}