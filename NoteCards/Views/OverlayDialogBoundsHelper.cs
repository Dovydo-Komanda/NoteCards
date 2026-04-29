using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NoteCards.Views
{
    internal static class OverlayDialogBoundsHelper
    {
        private const int DwmwaExtendedFrameBounds = 9;

        public static void Apply(Window dialog)
        {
            if (dialog.Owner is null)
                return;

            var ownerHandle = new WindowInteropHelper(dialog.Owner).Handle;
            if (ownerHandle == IntPtr.Zero || !TryGetOwnerDeviceBounds(ownerHandle, out var rect))
            {
                ApplyFromOwnerDipBounds(dialog);
                return;
            }

            var transformFromDevice = ResolveTransformFromDevice(ownerHandle, dialog.Owner);
            var topLeft = transformFromDevice.Transform(new Point(rect.Left, rect.Top));
            var bottomRight = transformFromDevice.Transform(new Point(rect.Right, rect.Bottom));
            var width = Math.Max(0, bottomRight.X - topLeft.X);
            var height = Math.Max(0, bottomRight.Y - topLeft.Y);

            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = topLeft.X;
            dialog.Top = topLeft.Y;
            dialog.Width = width;
            dialog.Height = height;
        }

        private static bool TryGetOwnerDeviceBounds(IntPtr ownerHandle, out RECT rect)
        {
            if (DwmGetWindowAttribute(
                    ownerHandle,
                    DwmwaExtendedFrameBounds,
                    out rect,
                    Marshal.SizeOf<RECT>()) == 0)
            {
                return true;
            }

            return GetWindowRect(ownerHandle, out rect);
        }

        private static Matrix ResolveTransformFromDevice(IntPtr ownerHandle, Window owner)
        {
            if (HwndSource.FromHwnd(ownerHandle)?.CompositionTarget is { } hwndTarget)
                return hwndTarget.TransformFromDevice;

            if (PresentationSource.FromVisual(owner)?.CompositionTarget is { } visualTarget)
                return visualTarget.TransformFromDevice;

            return Matrix.Identity;
        }

        private static void ApplyFromOwnerDipBounds(Window dialog)
        {
            var owner = dialog.Owner;
            if (owner is null)
                return;

            var width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
            var height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
            if (double.IsNaN(width) || width <= 0 || double.IsNaN(height) || height <= 0)
                return;

            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = owner.Left;
            dialog.Top = owner.Top;
            dialog.Width = width;
            dialog.Height = height;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out RECT pvAttribute,
            int cbAttribute);

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
