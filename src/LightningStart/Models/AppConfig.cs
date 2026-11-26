namespace LightningStart.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public BehaviorConfig Behavior { get; set; } = new();

    public List<AppEntry> Entries { get; set; } = new();
}
