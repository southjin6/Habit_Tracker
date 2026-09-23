using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HabitTracker.App.Views;

/// <summary>
/// Asks the desktop window manager to paint the OS-drawn caption to match the app palette. Without
/// it the window body is dark but the title bar stays the system colour, and on Windows 11 the
/// "show accent colour on title bars" setting paints the caption even after immersive dark mode.
/// </summary>
internal static class DarkTitleBar
{
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModePre2021 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void ApplyTo(Window window)
    {
        // The handle only exists once the source is initialized, so the call has to wait for it.
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, ImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, ImmersiveDarkModePre2021, ref on, sizeof(int));
            }

            // Explicit colours win over the system accent setting; unsupported before Windows 11.
            SetColor(hwnd, DwmwaCaptionColor, "WindowBgBrush");
            SetColor(hwnd, DwmwaBorderColor, "CardBorderBrush");
            SetColor(hwnd, DwmwaTextColor, "TextPrimaryBrush");
        };
    }

    private static void SetColor(IntPtr hwnd, int attribute, string paletteKey)
    {
        if (Application.Current.Resources[paletteKey] is not SolidColorBrush brush)
        {
            return;
        }

        var color = brush.Color;
        var colorRef = (color.R << 16) | (color.G << 8) | color.B;
        DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
