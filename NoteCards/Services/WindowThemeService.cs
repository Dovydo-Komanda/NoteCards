using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NoteCards.Services;

internal static class WindowThemeService
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void ApplyTheme(Window window, string theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var useDarkMode = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var attributeSize = Marshal.SizeOf<int>();

        var result = DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkMode, ref useDarkMode, attributeSize);
        if (result != 0)
            DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkModeBefore20H1, ref useDarkMode, attributeSize);
    }
}
