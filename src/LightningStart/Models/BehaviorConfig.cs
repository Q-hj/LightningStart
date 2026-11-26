using LightningStart.Infrastructure;

namespace LightningStart.Models;

public sealed class BehaviorConfig : ObservableObject
{
    private string _trigger = "Hover";
    private int _hoverDelayMs = 150;
    private int _columns = 4;
    private int _iconSize = 24;
    private bool _showLabels = true;
    private bool _startWithWindows;

    /// <summary>"Hover" (open on tray hover) or "Click" (open on tray click).</summary>
    public string Trigger
    {
        get => _trigger;
        set => SetProperty(ref _trigger, value);
    }

    /// <summary>Extra delay after the shell reports a tray hover, in milliseconds.</summary>
    public int HoverDelayMs
    {
        get => _hoverDelayMs;
        set => SetProperty(ref _hoverDelayMs, value);
    }

    public int Columns
    {
        get => _columns;
        set => SetProperty(ref _columns, value);
    }

    public int IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public bool ShowLabels
    {
        get => _showLabels;
        set => SetProperty(ref _showLabels, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public void Normalize()
    {
        if (Trigger is not ("Hover" or "Click"))
            Trigger = "Hover";

        HoverDelayMs = Math.Clamp(HoverDelayMs, 0, 1000);
        Columns = Math.Clamp(Columns, 3, 6);
        IconSize = Math.Clamp(IconSize, 24, 96);
    }
}
