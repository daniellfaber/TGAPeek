using TgaViewer.TestSupport;

namespace TgaViewer.Core.Tests;

public sealed class TgaFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TgaViewer tests ✓ " + Guid.NewGuid().ToString("N"));

    public TgaFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, TgaSamples.Bytes(TgaSamples.TrueColor(24, origin: 2, width: 4, height: 3)));
        return path;
    }

    [Fact]
    public void LoadsPathsWithSpacesAndUnicode()
    {
        string path = Write("ünïcødé — test ✓ 1.tga");
        var image = TgaFile.Load(path);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, TgaFile.Probe(path).Header.Width);
    }

    [Fact]
    public void TheFileIsNotLockedAfterLoading()
    {
        string path = Write("delete me.tga");
        TgaFile.Load(path);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OtherProgramsCanWriteAndDeleteWhileTheFileIsOpen()
    {
        string path = Write("shared.tga");
        using var reading = TgaFile.Open(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
        }

        File.Delete(path);
    }

    [Fact]
    public void MissingFilesAndFolders_ThrowTheUsualExceptions()
    {
        Assert.Throws<FileNotFoundException>(() => TgaFile.Load(Path.Combine(_folder, "missing.tga")));
        Assert.Throws<DirectoryNotFoundException>(() => TgaFile.Load(Path.Combine(_folder, "no folder", "x.tga")));
        Assert.Throws<UnauthorizedAccessException>(() => TgaFile.Load(_folder));
    }

    [Fact]
    public void ListFolder_UsesNaturalOrder_AndOnlyTgaFiles()
    {
        foreach (string name in new[] { "a10.tga", "a2.tga", "B1.TGA", "c.png", "d.vst" })
        {
            File.WriteAllBytes(Path.Combine(_folder, name), [0]);
        }

        var names = TgaFile.ListFolder(Path.Combine(_folder, "a2.tga")).Select(Path.GetFileName);
        Assert.Equal(new[] { "a2.tga", "a10.tga", "B1.TGA", "d.vst" }, names);
    }

    [Fact]
    public void NaturalCompare_TreatsDigitRunsAsNumbers()
    {
        Assert.True(TgaFile.NaturalCompare("img2", "img10") < 0);
        Assert.True(TgaFile.NaturalCompare("img010", "img9") > 0);
        Assert.True(TgaFile.NaturalCompare("Apple", "banana") < 0);
        Assert.Equal(0, TgaFile.NaturalCompare("same", "same"));
    }

    [Fact]
    public void LoadErrorText_ExplainsEachKindOfFailure()
    {
        const string path = @"C:\images\brick wall.tga";
        string Describe(Exception ex) => LoadErrorText.Describe(ex, path);

        Assert.Contains("not a valid TGA", Describe(new TgaInvalidFileException(TgaErrorReason.UnknownImageType, "x")));
        Assert.Contains("not support", Describe(new TgaUnsupportedFormatException(TgaErrorReason.NoImageData, "x")));
        Assert.Contains("too large", Describe(new TgaLimitExceededException(TgaErrorReason.MemoryBudgetExceeded, "x", 2, 1)));
        Assert.Contains("could not be found", Describe(new FileNotFoundException()));
        Assert.Contains("denied", Describe(new UnauthorizedAccessException()));
        Assert.Contains("could not be read", Describe(new IOException("busy")));
        Assert.Contains("not enough memory", Describe(new OutOfMemoryException()));
        Assert.Contains("brick wall.tga", Describe(new IOException()));
        Assert.True(LoadErrorText.IsExpected(new IOException()));
        Assert.False(LoadErrorText.IsExpected(new NullReferenceException()));
    }
}
