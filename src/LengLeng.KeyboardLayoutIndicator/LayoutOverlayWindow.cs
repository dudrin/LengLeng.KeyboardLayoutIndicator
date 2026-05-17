using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class LayoutOverlayWindow : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private static readonly nint HwndTopmost = unchecked((nint)(-1));

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    public LayoutOverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(18, 18, 18);
        Opacity = 0.86;
        Size = new Size(220, 96);

        _label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Point)
        };

        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WsExTransparent
                | WsExToolWindow
                | WsExTopmost
                | WsExNoActivate;
            return createParams;
        }
    }

    public void ShowLayout(string text, Rectangle targetBounds, int durationMs)
    {
        if (string.IsNullOrWhiteSpace(text) || targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            return;
        }

        _label.Text = text;
        Bounds = GetCenteredBounds(targetBounds);

        if (!Visible)
        {
            Show();
        }

        SetWindowPos(
            Handle,
            HwndTopmost,
            Left,
            Top,
            Width,
            Height,
            SwpNoActivate | SwpShowWindow);

        _hideTimer.Stop();
        _hideTimer.Interval = Math.Clamp(durationMs, 300, 5000);
        _hideTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _label.Dispose();
        }

        base.Dispose(disposing);
    }

    private Rectangle GetCenteredBounds(Rectangle targetBounds)
    {
        var screenBounds = Screen.FromRectangle(targetBounds).Bounds;
        var width = Math.Min(Width, Math.Max(140, targetBounds.Width));
        var height = Math.Min(Height, Math.Max(70, targetBounds.Height));
        var left = targetBounds.Left + (targetBounds.Width - width) / 2;
        var top = targetBounds.Top + (targetBounds.Height - height) / 2;

        left = Math.Clamp(left, screenBounds.Left, screenBounds.Right - width);
        top = Math.Clamp(top, screenBounds.Top, screenBounds.Bottom - height);

        return new Rectangle(left, top, width, height);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
