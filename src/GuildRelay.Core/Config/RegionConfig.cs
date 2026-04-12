namespace GuildRelay.Core.Config;

public sealed record RegionConfig(
    int X, int Y, int Width, int Height,
    int CapturedAtDpi,
    ResolutionConfig CapturedAtResolution,
    string MonitorDeviceId)
{
    public static RegionConfig Empty => new(0, 0, 0, 0, 96, ResolutionConfig.Empty, string.Empty);
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record ResolutionConfig(int Width, int Height)
{
    public static ResolutionConfig Empty => new(0, 0);
}
