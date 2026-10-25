using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using TgaViewer.Core;

namespace TgaViewer.App;

/// <summary>The viewer window: image, compact menus and toolbar, status bar and an optional information panel.</summary>
internal sealed class MainForm : Form
{
    private const string AppName = "TGA Viewer";

    private readonly AppSettings _settings;
    private readonly string[] _startupPaths;
    private readonly ImageCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusFormat = new();
    private readonly ToolStripStatusLabel _statusAlpha = new();
    private readonly ToolStripStatusLabel _statusZoom = new();
    private readonly ToolStripStatusLabel _statusPixel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusWarnings = new() { IsLink = true };
    private readonly Panel _infoPanel = new() { Dock = DockStyle.Right };
    private readonly Splitter _splitter = new() { Dock = DockStyle.Right };
    private readonly TextBox _inspectorText = ReadOnlyText("Pixel inspector");
    private readonly TextBox _infoText = ReadOnlyText("Image information");
    private readonly PictureBox _stampBox = new() { Dock = DockStyle.Top, SizeMode = PictureBoxSizeMode.CenterImage, Height = 0 };
    private readonly Dictionary<ChannelView, ToolStripMenuItem> _channelMenu = [];
    private readonly Dictionary<ChannelView, ToolStripButton> _channelButtons = [];
    private readonly Dictionary<AlphaMode, ToolStripMenuItem> _alphaMenu = [];
    private readonly Dictionary<AlphaMode, ToolStripMenuItem> _alphaDropItems = [];
    private readonly ToolStripDropDownButton _alphaDrop = new("Alpha: Auto") { AccessibleName = "Alpha mode", ToolTipText = "How the alpha channel is applied" };
    private ToolStripMenuItem _nearestItem = null!;
    private ToolStripMenuItem _smoothItem = null!;
    private ToolStripMenuItem _checkerItem = null!;
    private ToolStripMenuItem _infoItem = null!;
    private ToolStripButton _smoothButton = null!;
    private ToolStripButton _checkerButton = null!;
    private ToolStripButton _infoButton = null!;

    private TgaImage? _image;
    private string? _path;
    private string? _requestedPath;
    private AlphaDecision? _decision;
    private AlphaMode _alphaMode = AlphaMode.Auto;
    private DisplayOptions _display;
    private IReadOnlyList<string> _navigation = [];
    private bool _navigationFromArguments;
    private CancellationTokenSource? _loadCancel;
    private int _loadGeneration;
    private Point? _pinned;
    private Point? _hovered;

    public MainForm(AppSettings settings, string[] startupPaths)
    {
        _settings = settings;
        _startupPaths = startupPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _display = new DisplayOptions
        {
            Scaling = settings.Scaling == nameof(ScalingMode.Smooth) ? ScalingMode.Smooth : ScalingMode.Nearest,
            Checkerboard = settings.Checkerboard,
            SolidBackground = ParseColor(settings.BackgroundColor),
        };

        Text = AppName;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimumSize = new Size(480, 360);
        ClientSize = new Size(1024, 720);
        AllowDrop = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        BuildInfoPanel();

        // Docking is applied from the last control added, so the canvas (first) fills what is left.
        Controls.Add(_canvas);
        Controls.Add(_splitter);
        Controls.Add(_infoPanel);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        Controls.Add(_status);
        MainMenuStrip = _menu;

        _canvas.SetOptions(_display);
        _canvas.AllowDrop = true;
        _canvas.DragEnter += (_, e) => OnDragEnter(e);
        _canvas.DragDrop += (_, e) => OnDragDrop(e);
        _canvas.PixelHovered += (_, e) =>
        {
            _hovered = e.Pixel;
            UpdatePixelInfo();
        };
        _canvas.PixelClicked += (_, e) =>
        {
            _pinned = e.Pixel;
            UpdatePixelInfo();
        };
        _canvas.ViewChanged += (_, _) => UpdateZoomStatus();

        StartPosition = FormStartPosition.WindowsDefaultLocation;
        ShowInfoPanel(settings.InfoPanelVisible);
        UpdateAll();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Restored after Windows Forms has applied DPI scaling, so saved sizes are not scaled again.
        RestoreWindowPlacement();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _canvas.Focus();
        if (_startupPaths.Length > 0)
        {
            OpenFile(_startupPaths[0], _startupPaths.Length > 1 ? _startupPaths : null);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _loadCancel?.Cancel();
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        _settings.InfoPanelVisible = _infoPanel.Visible;
        _settings.InfoPanelWidth = _infoPanel.Width;
        _settings.Scaling = _display.Scaling.ToString();
        _settings.Checkerboard = _display.Checkerboard;
        _settings.BackgroundColor = $"#{_display.SolidBackground & 0xFFFFFF:X6}";
        _settings.Save();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return;
        }

        Activate();
        if (paths.Length == 1 && Directory.Exists(paths[0]))
        {
            var images = TgaFile.ListFolder(Path.Combine(paths[0], "folder.tga"));
            if (images.Count > 0)
            {
                OpenFile(images[0]);
            }
            else
            {
                MessageBox.Show(this, $"The folder \"{paths[0]}\" contains no TGA images.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        OpenFile(paths[0], paths.Length > 1 ? paths : null);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Image shortcuts apply while the image has focus; text boxes keep their normal keys.
        if (ActiveControl is null || ActiveControl == _canvas)
        {
            int step = Math.Max(32, _canvas.ClientSize.Width / 8);
            switch (keyData)
            {
                case Keys.Left:
                case Keys.PageUp:
                    Navigate(-1);
                    return true;
                case Keys.Right:
                case Keys.PageDown:
                    Navigate(1);
                    return true;
                case Keys.Home:
                    NavigateTo(0);
                    return true;
                case Keys.End:
                    NavigateTo(_navigation.Count - 1);
                    return true;
                case Keys.Add:
                case Keys.Oemplus:
                case Keys.Shift | Keys.Oemplus:
                    _canvas.ZoomIn();
                    return true;
                case Keys.Subtract:
                case Keys.OemMinus:
                    _canvas.ZoomOut();
                    return true;
                case Keys.Control | Keys.Left:
                    _canvas.PanBy(step, 0);
                    return true;
                case Keys.Control | Keys.Right:
                    _canvas.PanBy(-step, 0);
                    return true;
                case Keys.Control | Keys.Up:
                    _canvas.PanBy(0, step);
                    return true;
                case Keys.Control | Keys.Down:
                    _canvas.PanBy(0, -step);
                    return true;
                case Keys.Escape:
                    _pinned = null;
                    UpdatePixelInfo();
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ----- Building the window -----

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Open…", Keys.Control | Keys.O, OpenWithDialog),
            new ToolStripSeparator(),
            Item("&Previous image", Keys.None, () => Navigate(-1), "Left, Page Up"),
            Item("&Next image", Keys.None, () => Navigate(1), "Right, Page Down"),
            new ToolStripSeparator(),
            Item("E&xit", Keys.None, Close, "Alt+F4"),
        });

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Fit to window", Keys.Control | Keys.D0, _canvas.FitToWindow),
            Item("&Actual pixels (100%)", Keys.Control | Keys.D1, () => _canvas.ActualPixels()),
            Item("Zoom &in", Keys.Control | Keys.Oemplus, () => _canvas.ZoomIn(), "Ctrl++, +, wheel up"),
            Item("Zoom &out", Keys.Control | Keys.OemMinus, () => _canvas.ZoomOut(), "Ctrl+−, −, wheel down"),
            new ToolStripSeparator(),
            ChannelMenu(),
            AlphaMenu(),
            ScalingMenu(),
            BackgroundMenu(),
            new ToolStripSeparator(),
            _infoItem = Item("&Information panel", Keys.F9, () => ShowInfoPanel(!_infoPanel.Visible)),
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Keyboard and mouse", Keys.F1, ShowShortcuts),
            Item("&About TGA Viewer", Keys.None, ShowAbout),
        });

        _menu.Items.AddRange(new ToolStripItem[] { file, view, help });
    }

    private ToolStripMenuItem ChannelMenu()
    {
        var menu = new ToolStripMenuItem("&Channel");
        foreach (var (channel, text, keys) in Channels())
        {
            var item = Item(text, keys, () => SetChannel(channel));
            _channelMenu[channel] = item;
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private static (ChannelView Channel, string Text, Keys Keys)[] Channels() =>
    [
        (ChannelView.Rgba, "RGB&A (alpha applied)", Keys.Control | Keys.D2),
        (ChannelView.Rgb, "&RGB (alpha not applied)", Keys.Control | Keys.D3),
        (ChannelView.Red, "R&ed channel", Keys.Control | Keys.D4),
        (ChannelView.Green, "&Green channel", Keys.Control | Keys.D5),
        (ChannelView.Blue, "&Blue channel", Keys.Control | Keys.D6),
        (ChannelView.Alpha, "Alpha channel &only", Keys.Control | Keys.D7),
    ];

    private ToolStripMenuItem AlphaMenu()
    {
        var menu = new ToolStripMenuItem("A&lpha");
        foreach (var (mode, text, keys) in AlphaModes())
        {
            var item = Item(text, keys, () => SetAlphaMode(mode));
            _alphaMenu[mode] = item;
            menu.DropDownItems.Add(item);

            var dropItem = new ToolStripMenuItem(text.Replace("&", ""), null, (_, _) => SetAlphaMode(mode));
            _alphaDropItems[mode] = dropItem;
            _alphaDrop.DropDownItems.Add(dropItem);
        }

        return menu;
    }

    private static (AlphaMode Mode, string Text, Keys Keys)[] AlphaModes() =>
    [
        (AlphaMode.Auto, "&Auto (follow the file)", Keys.Control | Keys.Shift | Keys.D1),
        (AlphaMode.Use, "&Use alpha", Keys.Control | Keys.Shift | Keys.D2),
        (AlphaMode.Ignore, "&Ignore alpha", Keys.Control | Keys.Shift | Keys.D3),
    ];

    private ToolStripMenuItem ScalingMenu()
    {
        var menu = new ToolStripMenuItem("&Scaling");
        _nearestItem = Item("&Nearest neighbour (crisp pixels)", Keys.Control | Keys.Shift | Keys.N, () => SetScaling(ScalingMode.Nearest));
        _smoothItem = Item("&Smooth", Keys.Control | Keys.Shift | Keys.S, () => SetScaling(ScalingMode.Smooth));
        menu.DropDownItems.AddRange(new ToolStripItem[] { _nearestItem, _smoothItem });
        return menu;
    }

    private ToolStripMenuItem BackgroundMenu()
    {
        var menu = new ToolStripMenuItem("&Background");
        _checkerItem = Item("&Checkerboard", Keys.Control | Keys.B, ToggleCheckerboard);
        menu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _checkerItem,
            new ToolStripSeparator(),
            Item("Solid &black", Keys.None, () => SetSolidBackground(Color.Black)),
            Item("Solid &grey", Keys.None, () => SetSolidBackground(Color.FromArgb(128, 128, 128))),
            Item("Solid &white", Keys.None, () => SetSolidBackground(Color.White)),
            Item("Solid colour…", Keys.None, ChooseBackgroundColor),
        });
        return menu;
    }

    private void BuildToolbar()
    {
        _toolbar.Items.Add(Button("Open", "Open an image (Ctrl+O)", OpenWithDialog));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("Fit", "Fit to window (Ctrl+0)", _canvas.FitToWindow));
        _toolbar.Items.Add(Button("100%", "Actual pixels (Ctrl+1)", () => _canvas.ActualPixels()));
        _toolbar.Items.Add(Button("−", "Zoom out (−)", () => _canvas.ZoomOut()));
        _toolbar.Items.Add(Button("+", "Zoom in (+)", () => _canvas.ZoomIn()));
        _toolbar.Items.Add(new ToolStripSeparator());
        foreach (var (channel, label, tip) in new[]
        {
            (ChannelView.Rgba, "RGBA", "Colour with alpha applied (Ctrl+2)"),
            (ChannelView.Rgb, "RGB", "Colour without alpha (Ctrl+3)"),
            (ChannelView.Red, "R", "Red channel as greyscale (Ctrl+4)"),
            (ChannelView.Green, "G", "Green channel as greyscale (Ctrl+5)"),
            (ChannelView.Blue, "B", "Blue channel as greyscale (Ctrl+6)"),
            (ChannelView.Alpha, "A", "Alpha channel as greyscale (Ctrl+7)"),
        })
        {
            var button = Button(label, tip, () => SetChannel(channel));
            _channelButtons[channel] = button;
            _toolbar.Items.Add(button);
        }

        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_alphaDrop);
        _smoothButton = Button("Smooth", "Smooth scaling on or off (Ctrl+Shift+S, Ctrl+Shift+N)", () =>
            SetScaling(_display.Scaling == ScalingMode.Smooth ? ScalingMode.Nearest : ScalingMode.Smooth));
        _checkerButton = Button("Checker", "Checkerboard behind transparency (Ctrl+B)", ToggleCheckerboard);
        _toolbar.Items.Add(_smoothButton);
        _toolbar.Items.Add(_checkerButton);
        _toolbar.Items.Add(new ToolStripSeparator());
        _infoButton = Button("Info", "Information panel (F9)", () => ShowInfoPanel(!_infoPanel.Visible));
        _toolbar.Items.Add(_infoButton);
    }

    private void BuildStatusBar()
    {
        _statusWarnings.Click += (_, _) => ShowInfoPanel(true, scrollToWarnings: true);
        _statusFormat.AccessibleName = "Image format";
        _statusAlpha.AccessibleName = "Alpha";
        _statusZoom.AccessibleName = "Zoom";
        _statusPixel.AccessibleName = "Pixel under the pointer";
        _statusWarnings.AccessibleName = "Warnings";
        _status.Items.AddRange(new ToolStripItem[] { _statusFormat, _statusAlpha, _statusZoom, _statusPixel, _statusWarnings });
        _status.ShowItemToolTips = true;
    }

    private void BuildInfoPanel()
    {
        _infoPanel.Width = Math.Clamp(_settings.InfoPanelWidth, 220, 1400);
        _inspectorText.Dock = DockStyle.Top;
        _inspectorText.Height = LogicalToDeviceUnits(150);
        _infoText.Dock = DockStyle.Fill;
        _infoPanel.Controls.Add(_infoText);
        _infoPanel.Controls.Add(_stampBox);
        _infoPanel.Controls.Add(Caption("Image information"));
        _infoPanel.Controls.Add(_inspectorText);
        _infoPanel.Controls.Add(Caption("Pixel (point at the image; click to pin, Esc to release)"));
    }

    private Label Caption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = LogicalToDeviceUnits(22),
        Padding = new Padding(4, 4, 0, 0),
        Font = new Font(Font, FontStyle.Bold),
    };

    private static TextBox ReadOnlyText(string name) => new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BorderStyle = BorderStyle.None,
        BackColor = SystemColors.Window,
        Font = new Font("Consolas", 9F),
        AccessibleName = name,
    };

    private static ToolStripMenuItem Item(string text, Keys keys, Action action, string? shortcutText = null)
    {
        var item = new ToolStripMenuItem(text);
        if (keys != Keys.None)
        {
            item.ShortcutKeys = keys;
        }

        if (shortcutText is not null)
        {
            item.ShortcutKeyDisplayString = shortcutText;
        }

        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripButton Button(string text, string tip, Action action)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip, AccessibleName = tip };
        button.Click += (_, _) => action();
        return button;
    }

    // ----- Opening files -----

    private TgaDecodeOptions DecodeOptions() => new()
    {
        AllowPartial = true,
        MaxDecodeMemoryBytes = Math.Max(64, _settings.MaxDecodeMemoryMB) * (1L << 20),
    };

    private void OpenWithDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open TGA image",
            Filter = "TGA images (*.tga;*.icb;*.vda;*.vst)|*.tga;*.icb;*.vda;*.vst|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (_path is not null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_path) ?? "";
        }

        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.FileNames.Length > 0)
        {
            OpenFile(dialog.FileNames[0], dialog.FileNames.Length > 1 ? dialog.FileNames : null);
        }
    }

    /// <summary>
    /// Loads a file in the background. Only the most recent request can change the window, and the
    /// current image is replaced only after the new one has decoded and rendered successfully.
    /// </summary>
    private async void OpenFile(string path, IReadOnlyList<string>? list = null)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            ErrorDialogs.ShowLoadError(this, path, ex);
            return;
        }

        _requestedPath = fullPath;
        if (list is not null)
        {
            _navigation = list.Select(TryGetFullPath).OfType<string>().ToList();
            _navigationFromArguments = true;
        }
        else if (!_navigationFromArguments || IndexIn(_navigation, fullPath) < 0)
        {
            _navigation = TgaFile.ListFolder(fullPath);
            _navigationFromArguments = false;
        }

        _loadCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _loadCancel = cancel;
        int generation = ++_loadGeneration;
        SetBusy($"Opening {Path.GetFileName(fullPath)}…");
        var options = DecodeOptions();
        try
        {
            var probe = await Task.Run(() => TgaFile.Probe(fullPath, options), cancel.Token);
            if (generation != _loadGeneration || !EnsureMemoryFor(probe, fullPath))
            {
                return;
            }

            var image = await Task.Run(() => TgaFile.Load(fullPath, options, cancel.Token), cancel.Token);
            if (generation != _loadGeneration)
            {
                return;
            }

            ShowImage(image, fullPath);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            if (ex is OutOfMemoryException or InsufficientMemoryException)
            {
                SystemMemory.Compact();
            }

            ErrorDialogs.ShowLoadError(this, fullPath, ex);
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                ClearBusy();
            }
        }
    }

    private void ShowImage(TgaImage image, string path)
    {
        var decision = AlphaPolicy.Resolve(image, _alphaMode);
        var previous = _image;
        _canvas.SetImage(image, decision.Interpretation); // renders first; on failure the old image stays
        _image = image;
        _path = path;
        _decision = decision;
        _pinned = null;
        _hovered = null;
        UpdateAll();
        if (previous is not null && previous.MemoryBytes > 64L << 20)
        {
            SystemMemory.Compact();
        }
    }

    /// <summary>
    /// Checks the estimated memory against what Windows can still provide. If it only fits without the
    /// current image, that image is closed first (and the user told); if it cannot fit, nothing changes.
    /// </summary>
    private bool EnsureMemoryFor(TgaProbe probe, string path)
    {
        long needed = probe.Memory.Total + TgaMemoryEstimator.EstimateViewportBytes(_canvas.ClientSize.Width, _canvas.ClientSize.Height);
        if (SystemMemory.AvailableCommitBytes() is not long available || needed <= available)
        {
            return true;
        }

        if (_image is not null && needed <= available + _image.MemoryBytes)
        {
            CloseImage();
            SystemMemory.Compact();
            SetBusy("The previous image was closed to free memory for this one…");
            return true;
        }

        MessageBox.Show(
            this,
            $"There is not enough free memory to open \"{Path.GetFileName(path)}\".\n\nIt needs about {ByteSize.Format(needed)}, "
                + $"but only about {ByteSize.Format(available)} is available. Close other programs and try again.",
            "Not enough memory",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private void CloseImage()
    {
        _canvas.SetImage(null, AlphaInterpretation.Opaque);
        _image = null;
        _path = null;
        _decision = null;
        UpdateAll();
    }

    private void Navigate(int delta)
    {
        if (_navigation.Count == 0 || _requestedPath is null)
        {
            return;
        }

        int index = IndexIn(_navigation, _requestedPath);
        int next = index < 0 ? 0 : index + delta;
        if (next < 0 || next >= _navigation.Count)
        {
            _statusPixel.Text = next < 0 ? "This is the first image." : "This is the last image.";
            return;
        }

        NavigateTo(next);
    }

    private void NavigateTo(int index)
    {
        if (index >= 0 && index < _navigation.Count)
        {
            OpenFile(_navigation[index], _navigationFromArguments ? _navigation : null);
        }
    }

    private static int IndexIn(IReadOnlyList<string> list, string path)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void SetBusy(string message)
    {
        UseWaitCursor = true;
        _statusPixel.Text = message;
    }

    private void ClearBusy()
    {
        UseWaitCursor = false;
        UpdatePixelInfo();
    }

    // ----- Display settings -----

    private void SetChannel(ChannelView channel)
    {
        _display = _display with { Channel = channel };
        _canvas.SetOptions(_display);
        UpdateChecks();
        UpdateCanvasMessages();
    }

    private void SetAlphaMode(AlphaMode mode)
    {
        _alphaMode = mode;
        if (_image is not null)
        {
            _decision = AlphaPolicy.Resolve(_image, mode);
            _canvas.SetAlpha(_decision.Interpretation);
        }

        UpdateAll();
    }

    private void SetScaling(ScalingMode scaling)
    {
        _display = _display with { Scaling = scaling };
        _canvas.SetOptions(_display);
        UpdateChecks();
    }

    private void ToggleCheckerboard()
    {
        _display = _display with { Checkerboard = !_display.Checkerboard };
        _canvas.SetOptions(_display);
        UpdateChecks();
    }

    private void SetSolidBackground(Color color)
    {
        _display = _display with { Checkerboard = false, SolidBackground = 0xFF000000 | (uint)(color.ToArgb() & 0xFFFFFF) };
        _canvas.SetOptions(_display);
        UpdateChecks();
    }

    private void ChooseBackgroundColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, Color = Color.FromArgb((int)(_display.SolidBackground | 0xFF000000)) };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetSolidBackground(dialog.Color);
        }
    }

    private void ShowInfoPanel(bool visible, bool scrollToWarnings = false)
    {
        _infoPanel.Visible = visible;
        _splitter.Visible = visible;
        UpdateChecks();
        if (visible)
        {
            UpdateInfo();
            UpdatePixelInfo();
            if (scrollToWarnings)
            {
                int at = _infoText.Text.IndexOf("WARNINGS", StringComparison.Ordinal);
                if (at >= 0)
                {
                    _infoText.Select(at, 0);
                    _infoText.ScrollToCaret();
                }
            }
        }
    }

    private static uint ParseColor(string text) =>
        text.StartsWith('#') && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? 0xFF000000 | (rgb & 0xFFFFFF)
            : 0xFF808080;

    private void RestoreWindowPlacement()
    {
        if (_settings.WindowWidth < 300 || _settings.WindowHeight < 200 || _settings.WindowX == int.MinValue)
        {
            return;
        }

        var bounds = new Rectangle(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
        if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
        {
            Bounds = bounds;
        }

        if (_settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    // ----- Updating the window -----

    private void UpdateAll()
    {
        UpdateTitle();
        UpdateStatus();
        UpdateChecks();
        UpdateCanvasMessages();
        UpdateInfo();
        UpdatePixelInfo();
    }

    private void UpdateTitle()
    {
        if (_path is null)
        {
            Text = AppName;
            return;
        }

        int index = IndexIn(_navigation, _path);
        string position = _navigation.Count > 1 && index >= 0 ? $" ({index + 1} of {_navigation.Count})" : "";
        Text = $"{Path.GetFileName(_path)}{position} — {AppName}";
    }

    private void UpdateStatus()
    {
        if (_image is null || _decision is null)
        {
            _statusFormat.Text = "No image";
            _statusAlpha.Text = "";
            _statusAlpha.ToolTipText = "";
            _statusWarnings.Text = "";
            _statusWarnings.ToolTipText = "";
            UpdateZoomStatus();
            return;
        }

        _statusFormat.Text = $"{_image.Width} × {_image.Height} · {_image.FormatDescription}";
        _statusFormat.ToolTipText = $"Image type {_image.Header.ImageType}, {_image.Header.PixelDepth}-bit pixels, first pixel at the {_image.Header.OriginName}";
        _statusAlpha.Text = "Alpha: " + _decision.Summary;
        _statusAlpha.ToolTipText = _decision.Explanation;

        int problems = _image.Warnings.Count(w => !w.IsNote);
        int notes = _image.Warnings.Count - problems;
        _statusWarnings.Text = _image.IsPartial
            ? $"⚠ Partial image ({(double)_image.DecodedPixelCount / _image.PixelCount:P0})"
            : problems > 0 ? $"⚠ {problems} warning{(problems == 1 ? "" : "s")}"
            : notes > 0 ? $"{notes} note{(notes == 1 ? "" : "s")}"
            : "";
        _statusWarnings.ToolTipText = string.Join(Environment.NewLine, _image.Warnings.Select(w => w.Message));
        UpdateZoomStatus();
    }

    private void UpdateZoomStatus()
    {
        _statusZoom.Text = _image is null ? "" : $"{_canvas.Zoom * 100:0.##}%{(_canvas.IsFitMode ? " (fit)" : "")}";
    }

    private void UpdateChecks()
    {
        foreach (var (channel, item) in _channelMenu)
        {
            item.Checked = channel == _display.Channel;
            _channelButtons[channel].Checked = item.Checked;
        }

        foreach (var (mode, item) in _alphaMenu)
        {
            item.Checked = mode == _alphaMode;
            _alphaDropItems[mode].Checked = item.Checked;
        }

        _alphaDrop.Text = "Alpha: " + _alphaMode;
        _nearestItem.Checked = _display.Scaling == ScalingMode.Nearest;
        _smoothItem.Checked = _display.Scaling == ScalingMode.Smooth;
        _smoothButton.Checked = _smoothItem.Checked;
        _checkerItem.Checked = _display.Checkerboard;
        _checkerButton.Checked = _display.Checkerboard;
        _infoItem.Checked = _infoPanel.Visible;
        _infoButton.Checked = _infoPanel.Visible;
    }

    private void UpdateCanvasMessages()
    {
        var lines = new List<string>();
        if (_image is { IsPartial: true })
        {
            lines.Add($"Partial image: the file ends after {(double)_image.DecodedPixelCount / _image.PixelCount:P0} of its pixels. Missing pixels have magenta stripes.");
        }

        if (_image?.InvalidIndexPixels is { } invalid)
        {
            lines.Add($"{invalid.CountSet():N0} pixels use palette indices outside the colour map. They have orange stripes.");
        }

        string? overlay = _image is not null && _display.Channel == ChannelView.Alpha && !_image.Alpha.HasStoredAlpha
            ? "This file stores no alpha channel, so there is nothing to show in the alpha view."
            : null;
        _canvas.SetMessages(lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null, overlay);
    }

    private void UpdateInfo()
    {
        if (!_infoPanel.Visible)
        {
            return;
        }

        var oldStamp = _stampBox.Image;
        _stampBox.Image = null;
        oldStamp?.Dispose();
        _stampBox.Height = 0;

        if (_image is null || _decision is null)
        {
            _infoText.Text = "No image is open.";
            return;
        }

        _infoText.Text = ImageReport.Build(_image, _path, _decision).ReplaceLineEndings("\r\n");
        if (_image.Metadata.PostageStamp.Value is { Width: > 0, Height: > 0 } stamp)
        {
            _stampBox.Image = StampBitmap(stamp, _decision.Interpretation);
            _stampBox.Height = stamp.Height + LogicalToDeviceUnits(12);
        }
    }

    private void UpdatePixelInfo()
    {
        var pixel = _pinned ?? _hovered;
        if (_image is null || _decision is null || pixel is not { } p)
        {
            _statusPixel.Text = _image is null ? "" : "Point at the image to inspect a pixel.";
            _inspectorText.Text = "";
            return;
        }

        _statusPixel.Text = (_pinned is not null ? "Pinned: " : "") + PixelInspector.Summarise(_image, p.X, p.Y);
        if (_infoPanel.Visible)
        {
            _inspectorText.Text = PixelInspector.Describe(_image, p.X, p.Y, _decision).ReplaceLineEndings("\r\n");
        }
    }

    /// <summary>The postage stamp at its stored size, composited over a small checkerboard.</summary>
    private static Bitmap StampBitmap(TgaPostageStamp stamp, AlphaInterpretation alpha)
    {
        var bitmap = new Bitmap(stamp.Width, stamp.Height, PixelFormat.Format32bppRgb);
        for (int y = 0; y < stamp.Height; y++)
        {
            for (int x = 0; x < stamp.Width; x++)
            {
                uint background = ((x / 4 + y / 4) & 1) == 0 ? 0xFFFFFFFF : 0xFFCCCCCC;
                uint shown = DisplayRenderer.Composite(stamp.Pixels[y * stamp.Width + x], alpha, background);
                bitmap.SetPixel(x, y, Color.FromArgb(unchecked((int)shown)));
            }
        }

        return bitmap;
    }

    // ----- Help -----

    private void ShowShortcuts()
    {
        MessageBox.Show(
            this,
            """
            Open                     Ctrl+O, or drop files onto the window
            Previous / next image    Left / Right, Page Up / Page Down
            First / last image       Home / End

            Fit to window            Ctrl+0, or double-click
            Actual pixels (100%)     Ctrl+1, or double-click when fitted
            Zoom in / out            Mouse wheel (zooms around the pointer), + / −, Ctrl++ / Ctrl+−
            Pan                      Drag with the left button, or Ctrl+arrow keys

            RGBA (alpha applied)     Ctrl+2
            RGB (alpha not applied)  Ctrl+3
            Red / green / blue       Ctrl+4 / Ctrl+5 / Ctrl+6
            Alpha only               Ctrl+7
            Alpha: auto / use / ignore   Ctrl+Shift+1 / 2 / 3
            Nearest / smooth scaling Ctrl+Shift+N / Ctrl+Shift+S
            Checkerboard on / off    Ctrl+B
            Information panel        F9

            Inspect a pixel          Point at it; click to pin it, Esc to release
            """,
            "Keyboard and mouse",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        MessageBox.Show(
            this,
            $"""
            TGA Viewer {version}

            A lightweight viewer for TGA (Truevision Targa) images, for inspecting textures.
            It never changes or saves image files, never uses the network, and collects no data.

            Settings: {AppSettings.FilePath}
            .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}
            """,
            "About TGA Viewer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
