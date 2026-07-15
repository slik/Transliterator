using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Translit;

public sealed class TrayApp : ApplicationContext
{
    private const string MenuEnabled = "Enabled";
    private const string MenuAutostart = "Start with Windows";
    private const string MenuExit = "Exit";
    private const string TooltipOn = "Transliterator (ON)";
    private const string TooltipOff = "Transliterator (OFF)";
    private const string IconText = "Ук";

    private readonly KeyboardHook _hook;
    private readonly HotKey _hotKey;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autostartItem;

    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private IntPtr _hIconOn;
    private IntPtr _hIconOff;

    private bool _enabled;
    private bool _disposed;

    public TrayApp()
    {
        try {
            _hook = new KeyboardHook { Enabled = false };
            _hotKey = new HotKey();

            (_iconOn, _hIconOn) = BuildIcon(Color.White); // white text = ON
            (_iconOff, _hIconOff) = BuildIcon(Color.FromArgb(0x80, 0x80, 0x80)); // grey text = OFF
        } catch {
            _hook?.Dispose();
            _hotKey?.Dispose();
            _iconOn?.Dispose();
            _iconOff?.Dispose();
            if (_hIconOn != IntPtr.Zero) DestroyIcon(_hIconOn);
            if (_hIconOff != IntPtr.Zero) DestroyIcon(_hIconOff);
            throw;
        }

        _enabledItem = new ToolStripMenuItem(MenuEnabled, null, (_, _) => Toggle()) {
            CheckOnClick = false,
            Checked = false,
        };
        _autostartItem = new ToolStripMenuItem(MenuAutostart, null, OnToggleAutostart) {
            CheckOnClick = false,
            Checked = Autostart.IsEnabled(),
        };
        var exitItem = new ToolStripMenuItem(MenuExit, null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon {
            Icon = _iconOff,
            Text = TooltipOff,
            Visible = true,
            ContextMenuStrip = menu,
        };

        _hotKey.Pressed += Toggle;

        ApplyState();
    }

    private void Toggle()
    {
        _enabled = !_enabled;
        ApplyState();
    }

    private void ApplyState()
    {
        _hook.Enabled = _enabled;
        _enabledItem.Checked = _enabled;
        _notifyIcon.Icon = _enabled ? _iconOn : _iconOff;
        _notifyIcon.Text = _enabled ? TooltipOn : TooltipOff;
    }

    private void OnToggleAutostart(object? sender, EventArgs e)
    {
        if (Autostart.IsEnabled())
            Autostart.Disable();
        else
            Autostart.Enable();
        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static (Icon icon, IntPtr hIcon) BuildIcon(Color textColor)
    {
        const int size = 64;
        const float margin = 0f;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            using var family = new FontFamily("Segoe UI");
            path.AddString(IconText, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
            var b = path.GetBounds();
            var scale = (size - 2 * margin) / Math.Max(b.Width, b.Height);
            using var m = new Matrix();
            m.Translate(size / 2f, size / 2f);
            m.Scale(scale, scale);
            m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
            path.Transform(m);
            using var brush = new SolidBrush(textColor);
            g.FillPath(brush, path);
        }

        var hIcon = bmp.GetHicon();
        using var tmp = Icon.FromHandle(hIcon);
        var icon = (Icon)tmp.Clone();
        return (icon, hIcon);
    }

    protected override void ExitThreadCore()
    {
        try {
            _notifyIcon.Visible = false;
        } catch {
            /* nothing */
        }

        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) {
            _disposed = true;
            _notifyIcon.Visible = false;
            _hotKey.Pressed -= Toggle;
            _hook.Dispose();
            _hotKey.Dispose();
            _notifyIcon.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
            if (_hIconOn != IntPtr.Zero) DestroyIcon(_hIconOn);
            if (_hIconOff != IntPtr.Zero) DestroyIcon(_hIconOff);
            _hIconOn = _hIconOff = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}