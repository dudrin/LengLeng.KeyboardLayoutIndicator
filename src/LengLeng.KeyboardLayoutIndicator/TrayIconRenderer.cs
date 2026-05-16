using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class TrayIconRenderer
{
    public static Icon CreateIcon(bool isEnglish, string indicatorKey)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var background = isEnglish ? Color.FromArgb(24, 128, 72) : Color.FromArgb(190, 92, 28);
        using var backgroundBrush = new SolidBrush(background);
        graphics.FillEllipse(backgroundBrush, 1, 1, 30, 30);

        using var borderPen = new Pen(Color.White, 2);
        graphics.DrawEllipse(borderPen, 2, 2, 28, 28);

        var text = isEnglish ? "EN" : "RU";
        using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, textBrush, (32 - size.Width) / 2, 8);

        using var keyFont = new Font("Segoe UI", 6, FontStyle.Bold, GraphicsUnit.Pixel);
        var keyText = LockKeyCatalog.Normalize(indicatorKey) switch
        {
            LockKeyCatalog.CapsLock => "C",
            LockKeyCatalog.NumLock => "N",
            _ => "S"
        };
        graphics.DrawString(keyText, keyFont, textBrush, 13, 21);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint handle);
}
