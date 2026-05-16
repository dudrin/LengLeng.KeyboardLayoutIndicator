using System.Drawing;
using System.Text.Json.Serialization;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class ScreenRectangle
{
    [JsonPropertyName("left")]
    public int Left { get; set; }

    [JsonPropertyName("top")]
    public int Top { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonIgnore]
    public bool IsValid => Width >= 8 && Height >= 8;

    public static ScreenRectangle FromRectangle(Rectangle rectangle)
    {
        return new ScreenRectangle
        {
            Left = rectangle.Left,
            Top = rectangle.Top,
            Width = rectangle.Width,
            Height = rectangle.Height
        };
    }

    public Rectangle ToRectangle()
    {
        return new Rectangle(Left, Top, Width, Height);
    }
}
