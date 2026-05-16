using System.Globalization;
using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class SystemInputMethodLanguageReader
{
    private const int RoInitMultithreaded = 1;
    private const int RpcChangedMode = unchecked((int)0x80010106);
    private const string LanguageRuntimeClassName = "Windows.Globalization.Language";

    private static readonly Guid LanguageStaticsInterfaceId =
        new("b23cd557-0865-46d4-89b8-d59be8990f0d");

    public LayoutSnapshot GetCurrentLayout()
    {
        var tag = GetCurrentInputMethodLanguageTag();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return LayoutSnapshot.Unknown;
        }

        try
        {
            var culture = CultureInfo.CreateSpecificCulture(tag);
            return new LayoutSnapshot(
                true,
                culture.Name,
                culture.TwoLetterISOLanguageName,
                culture.LCID,
                0);
        }
        catch (CultureNotFoundException)
        {
            var normalizedTag = tag.Trim();
            var language = normalizedTag.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? normalizedTag;

            return new LayoutSnapshot(
                true,
                normalizedTag,
                language,
                0,
                0);
        }
    }

    private static string? GetCurrentInputMethodLanguageTag()
    {
        nint className = 0;
        nint languageStatics = 0;
        nint languageTag = 0;

        try
        {
            var initializeResult = RoInitialize(RoInitMultithreaded);
            if (initializeResult < 0 && initializeResult != RpcChangedMode)
            {
                return null;
            }

            var result = WindowsCreateString(
                LanguageRuntimeClassName,
                LanguageRuntimeClassName.Length,
                out className);

            if (result < 0)
            {
                return null;
            }

            var interfaceId = LanguageStaticsInterfaceId;
            result = RoGetActivationFactory(className, ref interfaceId, out languageStatics);
            if (result < 0 || languageStatics == 0)
            {
                return null;
            }

            var virtualTable = Marshal.ReadIntPtr(languageStatics);
            var getCurrentInputMethodLanguageTagPointer = Marshal.ReadIntPtr(virtualTable, IntPtr.Size * 7);
            var getCurrentInputMethodLanguageTag =
                Marshal.GetDelegateForFunctionPointer<GetCurrentInputMethodLanguageTagDelegate>(
                    getCurrentInputMethodLanguageTagPointer);

            result = getCurrentInputMethodLanguageTag(languageStatics, out languageTag);
            if (result < 0 || languageTag == 0)
            {
                return null;
            }

            var rawString = WindowsGetStringRawBuffer(languageTag, out var length);
            return rawString == 0 || length == 0
                ? null
                : Marshal.PtrToStringUni(rawString, unchecked((int)length));
        }
        catch (Exception ex)
        {
            FileLog.Write("agent", "Cannot read current input method language tag.", ex);
            return null;
        }
        finally
        {
            if (languageTag != 0)
            {
                WindowsDeleteString(languageTag);
            }

            if (languageStatics != 0)
            {
                Marshal.Release(languageStatics);
            }

            if (className != 0)
            {
                WindowsDeleteString(className);
            }
        }
    }

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        nint className,
        ref Guid interfaceId,
        out nint activationFactory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern nint WindowsGetStringRawBuffer(nint hstring, out uint length);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentInputMethodLanguageTagDelegate(nint instance, out nint languageTag);
}
