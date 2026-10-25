using System.Runtime.InteropServices;

namespace KeyboardLayoutFixer;

/// <summary>Win32 entry points. InstallLayoutOrTip is documented (Microsoft Learn, TSF); the bcp47langs
/// functions are undocumented but read-only and only used for reading the profile (registry fallback exists).</summary>
internal static class Native
{
    // InstallLayoutOrTip flags (documented, not in a public header)
    public const uint ILOT_UNINSTALL = 0x00000001;

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int processId);

    [DllImport("input.dll", EntryPoint = "InstallLayoutOrTip", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InstallLayoutOrTip(string psz, uint flags);

    [DllImport("bcp47langs.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserLanguages(char delimiter, out IntPtr languages);

    [DllImport("bcp47langs.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserLanguageInputMethods(string language, char delimiter, out IntPtr inputMethods);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    private static string ReadHString(IntPtr h)
    {
        if (h == IntPtr.Zero) return "";
        try
        {
            var buf = WindowsGetStringRawBuffer(h, out uint len);
            return Marshal.PtrToStringUni(buf, (int)len) ?? "";
        }
        finally { WindowsDeleteString(h); }
    }

    /// <summary>Profile languages (what Settings shows), e.g. ["en-GB", "ru"].</summary>
    public static string[] GetProfileLanguages()
    {
        if (GetUserLanguages(';', out var h) != 0) throw new InvalidOperationException("GetUserLanguages failed");
        return ReadHString(h).Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Input methods of one profile language, e.g. ["0809:00000809"].</summary>
    public static string[] GetProfileInputMethods(string language)
    {
        if (GetUserLanguageInputMethods(language, ';', out var h) != 0) throw new InvalidOperationException("GetUserLanguageInputMethods failed");
        return ReadHString(h).Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    public static IntPtr[] GetLiveLayoutHandles()
    {
        int n = GetKeyboardLayoutList(0, null);
        var buf = new IntPtr[n];
        n = GetKeyboardLayoutList(n, buf);
        return buf.Take(n).ToArray();
    }
}
