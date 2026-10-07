using NAudio.Wave;
using System.IO;

namespace AudioPausePlayer;

public static class SilenceDetector
{
    public static List<SilenceRange> Detect(string filePath)
    {
        return Detect(filePath, new SilenceSettings());
    }

    public static List<SilenceRange> Detect(string filePath, SilenceSettings settings)
    {
        settings.Validate();
        using var reader = new AudioFileReader(filePath);
        return DetectSamples(reader, settings);
    }

    internal static List<SilenceRange> DetectSamples(ISampleProvider source)
    {
        return DetectSamples(source, new SilenceSettings());
    }

    internal static List<SilenceRange> DetectSamples(ISampleProvider source, SilenceSettings settings)
    {
        settings.Validate();
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        int frameMilliseconds = settings.FrameMilliseconds;
        int minimumSilenceMilliseconds = settings.MinimumSilenceMilliseconds;
        int silenceThresholdDb = settings.SilenceThresholdDb;

        var buffer = new float[((rate * frameMilliseconds + 999) / 1000) * channels];
        var ranges = new List<SilenceRange>();
        long processedGroups = 0;
        long frameIndex = 0;
        long? silenceStart = null;
        long minimumGroups = ((long)rate * minimumSilenceMilliseconds + 999) / 1000;

        while (true)
        {
            // Derive boundaries from the absolute frame index to avoid rounding drift.
            long nextBoundary = (++frameIndex * rate * frameMilliseconds) / 1000;
            int requested = checked((int)(nextBoundary - processedGroups) * channels);
            int count = 0;
            while (count < requested)
            {
                int read = source.Read(buffer, count, requested - count);
                if (read == 0) break;
                count += read;
            }
            if (count == 0) break;
            if (count % channels != 0) throw new InvalidDataException("PCM 数据包含不完整的声道采样组。");

            long start = processedGroups;
            processedGroups += count / channels;
            double sum = 0;
            for (int i = 0; i < count; i++) sum += (double)buffer[i] * buffer[i];
            double rms = Math.Sqrt(sum / count);
            double db = rms <= 1e-7 ? -100.0 : 20.0 * Math.Log10(rms);

            if (db < silenceThresholdDb)
            {
                if (!silenceStart.HasValue) silenceStart = start;
            }
            else if (silenceStart.HasValue)
            {
                AddRange(ranges, silenceStart.Value, start, minimumGroups, rate);
                silenceStart = null;
            }
            if (count < requested) break;
        }
        if (silenceStart.HasValue) AddRange(ranges, silenceStart.Value, processedGroups, minimumGroups, rate);
        return ranges;
    }

    private static void AddRange(List<SilenceRange> ranges, long start, long end, long minimum, int rate)
    {
        if (end - start < minimum) return;
        ranges.Add(new SilenceRange
        {
            Start = TimeSpan.FromSeconds((double)start / rate),
            End = TimeSpan.FromSeconds((double)end / rate)
        });
    }

    public static TimeSpan? FindNext(List<SilenceRange> ranges, TimeSpan currentTime)
    {
        return FindNext(ranges, currentTime, new SilenceSettings());
    }

    public static TimeSpan? FindNext(List<SilenceRange> ranges, TimeSpan currentTime, SilenceSettings settings)
    {
        settings.Validate();
        TimeSpan limit = currentTime + TimeSpan.FromMilliseconds(settings.NavigationToleranceMilliseconds);
        foreach (var range in ranges)
            if (range.Middle > limit) return range.Middle;
        return null;
    }

    public static TimeSpan? FindPrevious(List<SilenceRange> ranges, TimeSpan currentTime)
    {
        return FindPrevious(ranges, currentTime, new SilenceSettings());
    }

    public static TimeSpan? FindPrevious(List<SilenceRange> ranges, TimeSpan currentTime, SilenceSettings settings)
    {
        settings.Validate();
        TimeSpan limit = currentTime - TimeSpan.FromMilliseconds(settings.NavigationToleranceMilliseconds);
        for (int i = ranges.Count - 1; i >= 0; i--)
            if (ranges[i].Middle < limit) return ranges[i].Middle;
        return null;
    }
}
