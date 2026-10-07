namespace AudioPausePlayer;

public sealed class SilenceSettings
{
    public const int DefaultFrameMilliseconds = 10;
    public const int DefaultMinimumSilenceMilliseconds = 300;
    public const int DefaultSilenceThresholdDb = -45;
    public const int DefaultNavigationToleranceMilliseconds = 200;

    public int FrameMilliseconds { get; set; } = DefaultFrameMilliseconds;
    public int MinimumSilenceMilliseconds { get; set; } = DefaultMinimumSilenceMilliseconds;
    public int SilenceThresholdDb { get; set; } = DefaultSilenceThresholdDb;
    public int NavigationToleranceMilliseconds { get; set; } = DefaultNavigationToleranceMilliseconds;

    public void Validate()
    {
        if (FrameMilliseconds < 1 || FrameMilliseconds > 100)
            throw new ArgumentOutOfRangeException(nameof(FrameMilliseconds), "分析帧长度必须在 1 到 100 ms 之间。");
        if (MinimumSilenceMilliseconds < 50 || MinimumSilenceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(MinimumSilenceMilliseconds), "最短停顿时长必须在 50 到 5000 ms 之间。");
        if (SilenceThresholdDb < -100 || SilenceThresholdDb > 0)
            throw new ArgumentOutOfRangeException(nameof(SilenceThresholdDb), "静音阈值必须在 -100 到 0 dBFS 之间。");
        if (NavigationToleranceMilliseconds < 1 || NavigationToleranceMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(NavigationToleranceMilliseconds), "导航容差必须在 1 到 5000 ms 之间。");
    }
}
