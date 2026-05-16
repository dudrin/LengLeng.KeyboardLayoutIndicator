namespace LengLeng.KeyboardLayoutIndicator;

internal readonly record struct LayoutSnapshot(
    bool IsKnown,
    string CultureName,
    string TwoLetterLanguageName,
    int LanguageId,
    nint KeyboardLayoutHandle)
{
    public static LayoutSnapshot Unknown { get; } = new(false, "unknown", "unknown", 0, 0);

    public string DisplayName =>
        IsKnown
            ? $"{CultureName} (0x{LanguageId:X4})"
            : "unknown";
}
