namespace QueueServer.Core.Models;

/// <summary>
/// Key-value settings (Prefix, RunningText, LogoPath, dsb).
/// </summary>
public class Setting
{
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}