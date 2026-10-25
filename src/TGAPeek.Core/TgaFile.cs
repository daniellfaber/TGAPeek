namespace TGAPeek.Core;

/// <summary>Opens TGA files read-only, without locking them for other programs.</summary>
public static class TgaFile
{
    /// <summary>File extensions used for Truevision TGA images.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".tga", ".icb", ".vda", ".vst"];

    public static TgaProbe Probe(string path, TgaDecodeOptions? options = null)
    {
        using var stream = Open(path);
        return TgaDecoder.Probe(stream, options);
    }

    public static TgaImage Load(string path, TgaDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var stream = Open(path);
        return TgaDecoder.Decode(stream, options, cancellationToken);
    }

    /// <summary>
    /// Read-only, and sharing read, write and delete access so the viewer never blocks other
    /// programs. The decoder does its own buffering, so the stream is unbuffered.
    /// </summary>
    public static FileStream Open(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 1,
        FileOptions.SequentialScan);

    public static bool HasTgaExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The TGA files in the folder containing <paramref name="path"/>, in natural (Explorer-like) order.</summary>
    public static IReadOnlyList<string> ListFolder(string path)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is null || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            var files = Directory.EnumerateFiles(folder).Where(HasTgaExtension).ToList();
            files.Sort((a, b) => NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));
            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Compares names with digit runs as numbers ("img2" before "img10"), ignoring case.</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int startA = i, startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                ReadOnlySpan<char> numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                ReadOnlySpan<char> numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                int compare = numberA.Length != numberB.Length
                    ? numberA.Length.CompareTo(numberB.Length)
                    : numberA.SequenceCompareTo(numberB);
                if (compare != 0)
                {
                    return compare;
                }
            }
            else
            {
                int compare = string.Compare(a, i, b, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (compare != 0)
                {
                    return compare;
                }

                i++;
                j++;
            }
        }

        int lengthCompare = (a.Length - i).CompareTo(b.Length - j);
        return lengthCompare != 0 ? lengthCompare : string.Compare(a, b, StringComparison.Ordinal);
    }
}
