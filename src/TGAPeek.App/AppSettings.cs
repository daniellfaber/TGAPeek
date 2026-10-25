using System.Text.Json;

namespace TGAPeek.App;

/// <summary>A few remembered preferences, stored as JSON in %LOCALAPPDATA%\TGAPeek\settings.json.</summary>
internal sealed class AppSettings
{
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool InfoPanelVisible { get; set; }
    public int InfoPanelWidth { get; set; } = 400;
    public string Scaling { get; set; } = "Nearest";
    public bool Checkerboard { get; set; } = true;
    public string BackgroundColor { get; set; } = "#808080";

    /// <summary>Memory budget for decoding one image, in MB (default 4096).</summary>
    public int MaxDecodeMemoryMB { get; set; } = 4096;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TGAPeek", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
