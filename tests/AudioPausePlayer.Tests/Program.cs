using AudioPausePlayer;
using NAudio.Wave;
using System.IO;

int failures = 0;
int passed = 0;
void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failures++; }
}
void Assert(bool value, string message = "Assertion failed")
{
    if (!value) throw new Exception(message);
}
List<SilenceRange> Detect(float[] samples, int rate = 1000, int channels = 1, int maxRead = int.MaxValue, SilenceSettings? settings = null)
{
    var source = new Samples(samples, rate, channels, maxRead);
    return settings is null ? SilenceDetector.DetectSamples(source) : SilenceDetector.DetectSamples(source, settings);
}
float[] Signal(int soundBefore, int silence, int soundAfter)
    => Enumerable.Repeat(0.5f, soundBefore).Concat(new float[silence]).Concat(Enumerable.Repeat(0.5f, soundAfter)).ToArray();


Check("Silence settings defaults", () =>
{
    var settings = new SilenceSettings();
    Assert(settings.FrameMilliseconds == 10);
    Assert(settings.MinimumSilenceMilliseconds == 300);
    Assert(settings.SilenceThresholdDb == -45);
    Assert(settings.NavigationToleranceMilliseconds == 200);
});
Check("Custom minimum silence is used", () =>
{
    var settings = new SilenceSettings { MinimumSilenceMilliseconds = 100 };
    Assert(Detect(new float[100], settings: settings).Count == 1);
});
Check("Custom silence threshold is used", () =>
{
    var settings = new SilenceSettings { SilenceThresholdDb = -50 };
    Assert(Detect(Enumerable.Repeat(0.005f, 400).ToArray(), settings: settings).Count == 0);
});
Check("299 ms is too short", () => Assert(Detect(new float[299]).Count == 0));
Check("300 ms qualifies at EOF", () => { var r = Detect(new float[300]); Assert(r.Count == 1 && r[0].Start == TimeSpan.Zero && r[0].End == TimeSpan.FromMilliseconds(300)); });
Check("No silence", () => Assert(Detect(Enumerable.Repeat(0.5f, 1000).ToArray()).Count == 0));
Check("Empty PCM", () => Assert(Detect(Array.Empty<float>()).Count == 0));
Check("Leading silence", () => { var r = Detect(Signal(0, 400, 100)); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(400)); });
Check("Interior silence", () => { var r = Detect(Signal(100, 400, 100)); Assert(r.Count == 1 && r[0].Middle == TimeSpan.FromMilliseconds(300)); });
Check("Partial final frame", () => { var r = Detect(Signal(100, 305, 0)); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(405)); });
Check("Short reads are not EOF", () => { var r = Detect(Signal(100, 400, 100), maxRead: 3); Assert(r.Count == 1 && r[0].Start == TimeSpan.FromMilliseconds(100) && r[0].End == TimeSpan.FromMilliseconds(500)); });
Check("Opposite stereo does not cancel", () => { float[] s = new float[1000]; for (int i = 0; i < s.Length; i++) s[i] = i % 2 == 0 ? 0.5f : -0.5f; Assert(Detect(s, channels: 2).Count == 0); });
Check("Stereo duration", () => { var r = Detect(new float[800], channels: 2, maxRead: 3); Assert(r.Count == 1 && r[0].End == TimeSpan.FromMilliseconds(400)); });
Check("Non integral frame sampling rate", () => { var r = Detect(new float[11025], rate: 11025, maxRead: 17); Assert(r.Count == 1 && r[0].End == TimeSpan.FromSeconds(1)); });
Check("Low energy below threshold", () => Assert(Detect(Enumerable.Repeat(0.005f, 400).ToArray()).Count == 1));
Check("Energy above threshold", () => Assert(Detect(Enumerable.Repeat(0.006f, 400).ToArray()).Count == 0));
var ranges = new List<SilenceRange> { new() { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) }, new() { Start = TimeSpan.FromSeconds(3), End = TimeSpan.FromSeconds(4) } };
Check("Next excludes exactly 200 ms", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1300)) == TimeSpan.FromMilliseconds(3500)));
Check("Next outside tolerance", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1299)) == TimeSpan.FromMilliseconds(1500)));
Check("Previous excludes exactly 200 ms", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(3700)) == TimeSpan.FromMilliseconds(1500)));
Check("Previous outside tolerance", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(3701)) == TimeSpan.FromMilliseconds(3500)));

Check("Custom navigation tolerance is used", () =>
{
    var settings = new SilenceSettings { NavigationToleranceMilliseconds = 1 };
    Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromMilliseconds(1300), settings) == TimeSpan.FromMilliseconds(1500));
    Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.FromMilliseconds(1700), settings) == TimeSpan.FromMilliseconds(1500));
});
Check("Navigation tolerance must be positive", () =>
{
    var settings = new SilenceSettings { NavigationToleranceMilliseconds = 0 };
    bool failed = false;
    try { settings.Validate(); }
    catch (ArgumentOutOfRangeException) { failed = true; }
    Assert(failed);
});
Check("No next retains position", () => Assert(SilenceDetector.FindNext(ranges, TimeSpan.FromSeconds(4)) is null));
Check("No previous retains position", () => Assert(SilenceDetector.FindPrevious(ranges, TimeSpan.Zero) is null));
Check("Empty navigation", () =>
{
    var empty = new List<SilenceRange>();
    Assert(SilenceDetector.FindNext(empty, TimeSpan.Zero) is null && SilenceDetector.FindPrevious(empty, TimeSpan.Zero) is null);
});

string audioFolder = Path.GetFullPath("audio");
string? sampleMp3 = Directory.Exists(audioFolder)
    ? Directory.GetFiles(audioFolder, "*.mp3").FirstOrDefault()
    : null;

string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    foreach (string name in new[] { "10.mp3", "2.MP3", "01.mp3", "3.mp3" })
        File.WriteAllText(Path.Combine(fixture, name), "not an mp3");

    Directory.CreateDirectory(Path.Combine(fixture, "nested"));
    File.WriteAllText(Path.Combine(fixture, "nested", "4.mp3"), "not an mp3");

    Check("Natural order, extension and non-recursive scan", () =>
    {
        var tracks = AudioLibrary.LoadFolder(fixture);
        Assert(string.Join(",", tracks.Select(t => Path.GetFileName(t.FilePath))) == "01.mp3,2.MP3,3.mp3,10.mp3");
    });

    Check("Broken file retained with error and unknown duration", () =>
    {
        var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "3.mp3");
        Assert(track.LoadError is not null && track.Duration is null && track.Title == "3" && track.DurationText == "未知");
    });

    Check("Metadata readers release broken files", () =>
    {
        AudioLibrary.LoadFolder(fixture);
        using var file = new FileStream(Path.Combine(fixture, "2.MP3"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert(file.Length > 0);
    });

    if (sampleMp3 is not null)
    {
        string valid = Path.Combine(fixture, "sample.mp3");
        File.Copy(sampleMp3, valid, true);

        using (var tags = TagLib.File.Create(valid))
        {
            tags.Tag.Title = "Test title";
            tags.Tag.Album = "Test album";
            tags.Save();
        }

        Check("Title and album metadata", () =>
        {
            var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "sample.mp3");
            Assert(track.Title == "Test title" && track.Album == "Test album" && track.Duration > TimeSpan.Zero);
        });

        using (var tags = TagLib.File.Create(valid))
        {
            tags.Tag.Title = " ";
            tags.Tag.Album = null;
            tags.Save();
        }

        Check("Missing metadata falls back", () =>
        {
            var track = AudioLibrary.LoadFolder(fixture).Single(t => Path.GetFileName(t.FilePath) == "sample.mp3");
            Assert(track.Title == "sample" && track.Album == "");
        });
    }
    else
    {
        Console.WriteLine("SKIP valid-MP3 metadata tests: add a local audio/*.mp3 file to enable them.");
    }

    Check("Missing folder reports error", () =>
    {
        try
        {
            AudioLibrary.LoadFolder(Path.Combine(fixture, "absent"));
            throw new Exception("No error");
        }
        catch (DirectoryNotFoundException)
        {
        }
    });
}
finally
{
    Directory.Delete(fixture, true);
}

Check("Active state follows playback flags", () =>
{
    var track = new AudioTrack { IsPaused = true };
    Assert(track.IsActive);
    track.IsPaused = false;
    Assert(!track.IsActive);
    track.IsPlaying = true;
    Assert(track.IsActive);
});

Check("Long duration formatting", () => Assert(AudioTrack.FormatTime(TimeSpan.FromSeconds(3661)) == "1:01:01"));

if (args.Contains("--report") && sampleMp3 is null)
    Console.WriteLine("SKIP --report: add local audio/*.mp3 files first.");
else if (args.Contains("--report"))
{
    string reportFolder = Path.GetFullPath("docs/test-results");
    Directory.CreateDirectory(reportFolder);
    var report = new System.Text.StringBuilder($"# 真实音频检测结果\n\n参数：{SilenceSettings.DefaultFrameMilliseconds} ms / "
        + $"{SilenceSettings.DefaultMinimumSilenceMilliseconds} ms / {SilenceSettings.DefaultSilenceThresholdDb} dBFS；"
        + $"导航容差 {SilenceSettings.DefaultNavigationToleranceMilliseconds} ms。\n\n"
        + "区间仅经自动数值检查，尚未经人工试听确认。\n\n");
    foreach (var track in AudioLibrary.LoadFolder(audioFolder))
    {
        Check("Real MP3: " + track.Title, () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var found = SilenceDetector.Detect(track.FilePath);
            watch.Stop();
            using (var reader = new AudioFileReader(track.FilePath))
            {
                foreach (var r in found) Assert(r.Start >= TimeSpan.Zero && r.End <= reader.TotalTime + TimeSpan.FromMilliseconds(1) && r.End - r.Start >= TimeSpan.FromMilliseconds(SilenceSettings.DefaultMinimumSilenceMilliseconds));
                Assert(found.Count > 0);
            }
            using (var file = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) Assert(file.Length > 0);
            report.AppendLine($"## {Path.GetFileName(track.FilePath)}\n\nTitle：{track.Title}；Album：{track.Album}；标签时长：{track.Duration}；分析耗时：{watch.Elapsed.TotalMilliseconds:F1} ms；有效停顿：{found.Count}。\n");
            report.AppendLine("| 起点（秒） | 终点（秒） | 中点（秒） |\n|---:|---:|---:|");
            foreach (var r in found) report.AppendLine($"| {r.Start.TotalSeconds:F3} | {r.End.TotalSeconds:F3} | {r.Middle.TotalSeconds:F3} |");
            report.AppendLine();
        });
    }
    File.WriteAllText(Path.Combine(reportFolder, "silence-report.md"), report.ToString());
}

try
{
    using var player = new AudioPlayer();
    await player.StopAsync(); await player.SeekAsync(TimeSpan.Zero); await player.PlayAsync(); player.Pause();
    Assert(!player.IsLoaded && player.PlaybackState == PlaybackState.Stopped);
    Console.WriteLine("PASS Unloaded playback actions"); passed++;
}
catch (Exception ex) { Console.WriteLine("FAIL Unloaded playback actions: " + ex.Message); failures++; }

if (args.Contains("--playback"))
{
    if (sampleMp3 is null)
    {
        Console.WriteLine("SKIP --playback: add local audio/*.mp3 files first.");
    }
    else
    {
        string playbackFixture = Path.Combine(AppContext.BaseDirectory, "playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(playbackFixture);
        try
        {
            string path = Path.Combine(playbackFixture, "first.mp3");
            string secondPath = Path.Combine(playbackFixture, "second.mp3");
            File.Copy(sampleMp3, path);
            File.Copy(sampleMp3, secondPath);

            using var player = new AudioPlayer();
            await player.OpenAsync(path);
            Assert(player.IsLoaded && player.GetDuration() > TimeSpan.Zero);

            await player.PlayAsync();
            await Task.Delay(150);
            Assert(player.PlaybackState == PlaybackState.Playing);

            player.Pause();
            Assert(player.PlaybackState == PlaybackState.Paused);

            await player.SeekAsync(TimeSpan.FromSeconds(2));
            Assert(player.PlaybackState == PlaybackState.Paused && Math.Abs(player.GetCurrentTime().TotalSeconds - 2) < 0.1);

            await player.PlayAsync();
            await Task.Delay(100);
            await player.SeekAsync(TimeSpan.FromSeconds(4));
            Assert(player.PlaybackState == PlaybackState.Playing);

            await player.StopAsync();
            Assert(player.PlaybackState == PlaybackState.Stopped && player.GetCurrentTime() == TimeSpan.Zero);

            await player.PlayAsync();
            await Task.WhenAll(player.StopAsync(), player.SeekAsync(TimeSpan.FromSeconds(2))).WaitAsync(TimeSpan.FromSeconds(5));
            Assert(player.PlaybackState == PlaybackState.Stopped && Math.Abs(player.GetCurrentTime().TotalSeconds - 2) < 0.1);
            Console.WriteLine("PASS Concurrent stop and seek are serialized");
            passed++;

            await player.SeekAsync(TimeSpan.FromSeconds(-1));
            Assert(player.GetCurrentTime() == TimeSpan.Zero);
            await player.SeekAsync(TimeSpan.MaxValue);
            Assert(player.GetCurrentTime() <= player.GetDuration());

            await player.OpenAsync(secondPath);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert(file.Length > 0);

            await player.CloseAsync();
            Assert(!player.IsLoaded);
            Console.WriteLine("PASS Playback lifecycle, seek and file release");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Playback integration: " + ex);
            failures++;
        }
        finally
        {
            Directory.Delete(playbackFixture, true);
        }
    }
}

if (args.Contains("--ui"))
{
    if (sampleMp3 is null)
    {
        Console.WriteLine("SKIP --ui: add local audio/*.mp3 files first.");
    }
    else
    {
        string uiFixture = Path.Combine(AppContext.BaseDirectory, "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uiFixture);
        try
        {
            File.Copy(sampleMp3, Path.Combine(uiFixture, "01.mp3"));
            File.Copy(sampleMp3, Path.Combine(uiFixture, "02.mp3"));
            File.Copy(sampleMp3, Path.Combine(uiFixture, "03.mp3"));
            failures += await UiChecks.RunAsync(uiFixture);
        }
        finally
        {
            Directory.Delete(uiFixture, true);
        }
    }
}

Console.WriteLine($"Tests: {passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

sealed class Samples : ISampleProvider
{
    private readonly float[] samples;
    private readonly int maxRead;
    private int position;

    public Samples(float[] samples, int rate, int channels, int maxRead)
    {
        this.samples = samples;
        this.maxRead = maxRead;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int remaining = samples.Length - position;
        int read = Math.Min(Math.Min(count, maxRead), remaining);
        Array.Copy(samples, position, buffer, offset, read);
        position += read;
        return read;
    }
}
