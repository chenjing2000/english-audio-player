using System.IO;

namespace AudioPausePlayer;

public static class AudioLibrary
{
    public static List<AudioTrack> LoadFolder(string folderPath)
    {
        string[] files = Directory.GetFiles(folderPath).Where(path =>
            string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase)).ToArray();
        Array.Sort(files, (left, right) => CompareNames(Path.GetFileName(left), Path.GetFileName(right)));
        var tracks = new List<AudioTrack>();
        foreach (string path in files)
        {
            string fallback = Path.GetFileNameWithoutExtension(path);
            try
            {
                using var file = TagLib.File.Create(path);
                tracks.Add(new AudioTrack
                {
                    FilePath = Path.GetFullPath(path),
                    Title = string.IsNullOrWhiteSpace(file.Tag.Title) ? fallback : file.Tag.Title,
                    Album = file.Tag.Album ?? "",
                    Duration = file.Properties.Duration
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TagLib.CorruptFileException || ex is TagLib.UnsupportedFormatException)
            {
                tracks.Add(new AudioTrack { FilePath = Path.GetFullPath(path), Title = fallback, LoadError = ex.Message });
            }
        }
        return tracks;
    }

    private static int CompareNames(string left, string right)
    {
        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
            {
                int aEnd = a;
                int bEnd = b;
                while (aEnd < left.Length && char.IsAsciiDigit(left[aEnd])) aEnd++;
                while (bEnd < right.Length && char.IsAsciiDigit(right[bEnd])) bEnd++;
                int aNumber = a;
                int bNumber = b;
                while (aNumber < aEnd - 1 && left[aNumber] == '0') aNumber++;
                while (bNumber < bEnd - 1 && right[bNumber] == '0') bNumber++;
                int lengthOrder = (aEnd - aNumber).CompareTo(bEnd - bNumber);
                if (lengthOrder != 0) return lengthOrder;
                for (int i = 0; i < aEnd - aNumber; i++)
                {
                    int digitOrder = left[aNumber + i].CompareTo(right[bNumber + i]);
                    if (digitOrder != 0) return digitOrder;
                }
                a = aEnd;
                b = bEnd;
            }
            else
            {
                int order = char.ToUpperInvariant(left[a]).CompareTo(char.ToUpperInvariant(right[b]));
                if (order != 0) return order;
                a++;
                b++;
            }
        }
        int remainingOrder = (left.Length - a).CompareTo(right.Length - b);
        return remainingOrder != 0 ? remainingOrder : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }
}
