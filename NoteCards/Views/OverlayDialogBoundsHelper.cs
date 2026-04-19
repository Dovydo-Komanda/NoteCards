using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NoteCards.Views
{
    internal static class OverlayDialogBoundsHelper
    {
        public static void Apply(Window dialog)
        {
            if (dialog.Owner is null)
                return;

            var ownerHandle = new WindowInteropHelper(dialog.Owner).Handle;
            if (ownerHandle == IntPtr.Zero || !GetWindowRect(ownerHandle, out var rect))
                return;

            var left = rect.Left;
            var top = rect.Top;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);

            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = left;
            dialog.Top = top;
            dialog.Width = width;
            dialog.Height = height;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
