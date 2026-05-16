namespace LengLeng.KeyboardLayoutIndicator;

internal static class LockKeyCatalog
{
    public const string CapsLock = "CapsLock";
    public const string NumLock = "NumLock";
    public const string ScrollLock = "ScrollLock";

    public static readonly string[] Names = { CapsLock, NumLock, ScrollLock };

    public static string Normalize(string? name)
    {
        if (string.Equals(name, CapsLock, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Caps", StringComparison.OrdinalIgnoreCase))
        {
            return CapsLock;
        }

        if (string.Equals(name, NumLock, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Num", StringComparison.OrdinalIgnoreCase))
        {
            return NumLock;
        }

        return ScrollLock;
    }

    public static ushort GetVirtualKey(string name)
    {
        return Normalize(name) switch
        {
            CapsLock => 0x14,
            NumLock => 0x90,
            _ => 0x91
        };
    }

    public static string GetDisplayName(string name)
    {
        return Normalize(name) switch
        {
            CapsLock => "Caps Lock",
            NumLock => "Num Lock",
            _ => "Scroll Lock"
        };
    }
}
