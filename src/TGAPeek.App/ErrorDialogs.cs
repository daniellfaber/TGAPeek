using TGAPeek.Core;

namespace TGAPeek.App;

internal static class ErrorDialogs
{
    public static void ShowLoadError(IWin32Window? owner, string path, Exception ex)
    {
        if (!LoadErrorText.IsExpected(ex))
        {
            ShowUnexpected(owner, ex);
            return;
        }

        var icon = ex is TgaUnsupportedFormatException ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
        MessageBox.Show(owner, LoadErrorText.Describe(ex, path), "Cannot open image", MessageBoxButtons.OK, icon);
    }

    public static void ShowUnexpected(IWin32Window? owner, Exception? ex)
    {
        string details = ex?.ToString() ?? "No details are available.";
        MessageBox.Show(
            owner,
            "Something went wrong inside TGAPeek. This is a bug in the viewer, not a problem with your file. Nothing was sent anywhere.\n\n"
                + "Press Ctrl+C to copy these details:\n\n" + details,
            "TGAPeek error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
