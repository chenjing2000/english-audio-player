namespace AudioPausePlayer;

public sealed class SilenceRange
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public TimeSpan Middle => Start + (End - Start) / 2;
}
