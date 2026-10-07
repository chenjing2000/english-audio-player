using NAudio.Wave;

namespace AudioPausePlayer;

public sealed class AudioPlayer : IDisposable
{
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private readonly SemaphoreSlim operations = new(1, 1);
    private TaskCompletionSource? playbackCompletion;
    private bool stopping;
    private PlaybackState state = PlaybackState.Stopped;
    private bool atEnd;
    private bool disposed;

    public bool IsLoaded => reader is not null;
    public bool IsPlaying => state == PlaybackState.Playing;
    public bool IsPaused => state == PlaybackState.Paused;

    // Kept internal for integration tests; UI code does not depend on NAudio state types.
    internal PlaybackState PlaybackState => state;
    public Exception? LastError { get; private set; }
    public event EventHandler? StateChanged;

    public Task OpenAsync(string filePath) => RunSerializedAsync(async () =>
    {
        await CloseCoreAsync();
        try
        {
            reader = new AudioFileReader(filePath);
            // Receive stop confirmation independently of UI dispatch.
            SynchronizationContext? context = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                output = new WaveOutEvent { DesiredLatency = 100 };
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
            output.PlaybackStopped += Output_PlaybackStopped;
            output.Init(reader);
            LastError = null;
            atEnd = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    });

    public Task PlayAsync() => RunSerializedAsync(async () =>
    {
        if (reader is null || output is null) return;
        if (output.PlaybackState == PlaybackState.Playing) return;
        if (output.PlaybackState == PlaybackState.Stopped) await StopOutputAsync();
        if (atEnd || reader.CurrentTime >= reader.TotalTime) reader.CurrentTime = TimeSpan.Zero;
        atEnd = false;
        LastError = null;
        StartOutput();
        state = PlaybackState.Playing;
        StateChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Pause()
    {
        if (output is null || state != PlaybackState.Playing) return;
        output.Pause();
        state = PlaybackState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StopAsync() => RunSerializedAsync(async () =>
    {
        await StopOutputAsync();
        if (reader is not null) reader.CurrentTime = TimeSpan.Zero;
        state = PlaybackState.Stopped;
        atEnd = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    });

    public Task SeekAsync(TimeSpan position) => RunSerializedAsync(async () =>
    {
        if (reader is null || output is null) return;
        PlaybackState previous = state;
        await StopOutputAsync();
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (position > reader.TotalTime) position = reader.TotalTime;
        reader.CurrentTime = position;
        atEnd = position >= reader.TotalTime;
        if (previous == PlaybackState.Playing && !atEnd) StartOutput();
        state = atEnd && previous == PlaybackState.Playing ? PlaybackState.Stopped : previous;
        StateChanged?.Invoke(this, EventArgs.Empty);
    });

    private void StartOutput()
    {
        if (output!.PlaybackState == PlaybackState.Stopped)
            playbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Play();
    }

    public TimeSpan GetCurrentTime() => atEnd && reader is not null ? reader.TotalTime : reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan GetDuration() => reader?.TotalTime ?? TimeSpan.Zero;

    private async Task StopOutputAsync()
    {
        TaskCompletionSource? completion = playbackCompletion;
        if (completion is null) return;
        stopping = true;
        try
        {
            if (output!.PlaybackState != PlaybackState.Stopped) output.Stop();
            // Stopped can precede the event: await this cycle in either case.
            await completion.Task.ConfigureAwait(false);
            playbackCompletion = null;
        }
        finally { stopping = false; }
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, output)) return;
        var completion = playbackCompletion;
        try
        {
            if (e.Exception is not null) LastError = e.Exception;
            if (!stopping)
            {
                state = PlaybackState.Stopped;
                atEnd = e.Exception is null;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { completion?.TrySetResult(); }
    }

    private async Task RunSerializedAsync(Func<Task> action)
    {
        await operations.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await action();
        }
        finally { operations.Release(); }
    }

    public Task CloseAsync() => RunSerializedAsync(CloseCoreAsync);

    private async Task CloseCoreAsync()
    {
        await StopOutputAsync();
        ReleaseResources();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseResources()
    {
        if (output is not null)
        {
            output.PlaybackStopped -= Output_PlaybackStopped;
            output.Dispose();
            output = null;
        }
        reader?.Dispose();
        reader = null;
        playbackCompletion = null;
        state = PlaybackState.Stopped;
        atEnd = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        // This callback needs no UI dispatch, so synchronous cleanup can wait.
        StopOutputAsync().GetAwaiter().GetResult();
        ReleaseResources();
        disposed = true;
        operations.Dispose();
    }
}
