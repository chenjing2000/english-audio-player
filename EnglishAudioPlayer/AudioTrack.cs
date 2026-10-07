using System.ComponentModel;

namespace AudioPausePlayer;

public sealed class AudioTrack : INotifyPropertyChanged
{
    public string FilePath { get; init; } = "";
    public string Title { get; init; } = "";
    public TimeSpan? Duration { get; init; }
    public string Album { get; init; } = "";
    public string? LoadError { get; init; }
    public string DurationText => Duration.HasValue ? FormatTime(Duration.Value) : "未知";

    private bool isPlaying;
    public bool IsPlaying
    {
        get => isPlaying;
        set
        {
            if (isPlaying == value) return;
            isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    private bool isPaused;
    public bool IsPaused
    {
        get => isPaused;
        set
        {
            if (isPaused == value) return;
            isPaused = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public bool IsActive => IsPlaying || IsPaused;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string FormatTime(TimeSpan time)
        => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
}
