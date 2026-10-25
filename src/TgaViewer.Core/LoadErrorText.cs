using System.Security;

namespace TgaViewer.Core;

/// <summary>Plain-language explanations of why a file could not be opened.</summary>
public static class LoadErrorText
{
    /// <summary>True for failures the viewer expects and explains; anything else is reported as a bug.</summary>
    public static bool IsExpected(Exception ex) => ex is TgaException
        or IOException
        or UnauthorizedAccessException
        or SecurityException
        or ArgumentException
        or NotSupportedException
        or OutOfMemoryException
        or InsufficientMemoryException;

    public static string Describe(Exception ex, string path)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string name = SafeFileName(path);
        return ex switch
        {
            TgaInvalidFileException => $"\"{name}\" is not a valid TGA image, or it is damaged.\n\n{ex.Message}",
            TgaUnsupportedFormatException => $"\"{name}\" uses a TGA variant this viewer does not support.\n\n{ex.Message}",
            TgaLimitExceededException => $"\"{name}\" is too large to open with the current memory limit.\n\n{ex.Message}\n\nThe limit (MaxDecodeMemoryMB) can be raised in the settings file.",
            FileNotFoundException or DirectoryNotFoundException => $"\"{name}\" could not be found.\n\n{path}",
            UnauthorizedAccessException or SecurityException => $"Windows denied access to \"{name}\".\n\n{ex.Message}",
            PathTooLongException => $"The path is too long to open.\n\n{path}",
            IOException => $"\"{name}\" could not be read.\n\n{ex.Message}",
            OutOfMemoryException or InsufficientMemoryException => $"There is not enough memory to open \"{name}\".",
            ArgumentException or NotSupportedException => $"\"{path}\" is not a valid file path.",
            _ => $"\"{name}\" could not be opened because of an unexpected error ({ex.GetType().Name}: {ex.Message}).",
        };
    }

    private static string SafeFileName(string path)
    {
        try
        {
            string name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
