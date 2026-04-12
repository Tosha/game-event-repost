using System;
using System.Runtime.InteropServices;

namespace GuildRelay.Platform.Windows.Dpi;

public static class DpiHelper
{
    public static int GetPrimaryMonitorDpi()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(hdc, LOGPIXELSX);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static (int Width, int Height) GetPrimaryScreenResolution()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    private const int LOGPIXELSX = 88;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
