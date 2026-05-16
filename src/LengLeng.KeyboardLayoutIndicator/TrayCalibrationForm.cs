using System.Drawing;
using System.Windows.Forms;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class TrayCalibrationForm : Form
{
    private Point _startPoint;
    private Rectangle _selection;
    private bool _selecting;

    public TrayCalibrationForm()
    {
        Bounds = SystemInformation.VirtualScreen;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.28;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    public Rectangle SelectedRectangle { get; private set; }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        base.OnKeyDown(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _startPoint = eventArgs.Location;
        _selection = Rectangle.Empty;
        _selecting = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (!_selecting)
        {
            return;
        }

        _selection = NormalizeRectangle(_startPoint, eventArgs.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (!_selecting || eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _selecting = false;
        _selection = NormalizeRectangle(_startPoint, eventArgs.Location);
        if (_selection.Width < 8 || _selection.Height < 8)
        {
            return;
        }

        SelectedRectangle = new Rectangle(
            Bounds.Left + _selection.Left,
            Bounds.Top + _selection.Top,
            _selection.Width,
            _selection.Height);
        DialogResult = DialogResult.OK;
        Hide();
        Close();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        using var textFont = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        eventArgs.Graphics.DrawString(
            "Переключитесь на ENG и выделите мышью область значка раскладки. Esc - отмена.",
            textFont,
            textBrush,
            24,
            24);

        if (_selection.IsEmpty)
        {
            return;
        }

        using var pen = new Pen(Color.White, 2);
        using var fill = new SolidBrush(Color.FromArgb(80, Color.White));
        eventArgs.Graphics.FillRectangle(fill, _selection);
        eventArgs.Graphics.DrawRectangle(pen, _selection);
    }

    private static Rectangle NormalizeRectangle(Point first, Point second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
