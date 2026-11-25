using System.Runtime.InteropServices;
using PeripheralBattery.Core;

namespace PeripheralBattery.Native;

internal static class IconPainter
{
    public static IntPtr Create(string label, BatteryTone tone)
    {
        const int size = 32;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = 40;
        info.bmiHeader.biWidth = size;
        info.bmiHeader.biHeight = -size;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        var color = CreateDIBSection(screen, ref info, 0, out _, IntPtr.Zero, 0);
        var previous = SelectObject(memory, color);
        var background = ColorRef(tone);
        var brush = CreateSolidBrush(background);
        var pen = CreatePen(0, 1, background);
        var oldBrush = SelectObject(memory, brush);
        var oldPen = SelectObject(memory, pen);
        Ellipse(memory, 0, 0, size, size);

        SetBkMode(memory, 1);
        SetTextColor(memory, 0x00FFFFFF);
        var fontHeight = label.Length >= 3 ? 12 : label.Length == 2 ? 15 : 18;
        var font = CreateFontW(-fontHeight, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var oldFont = SelectObject(memory, font);
        var rect = new RECT { Left = 0, Top = 1, Right = size, Bottom = size - 1 };
        DrawTextW(memory, label, label.Length, ref rect, 0x825);

        var maskBits = new byte[128];
        var mask = CreateBitmap(size, size, 1, 1, maskBits);

        SelectObject(memory, oldFont);
        SelectObject(memory, oldPen);
        SelectObject(memory, oldBrush);
        SelectObject(memory, previous);
        DeleteObject(font);
        DeleteObject(pen);
        DeleteObject(brush);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);

        var iconInfo = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var icon = CreateIconIndirect(ref iconInfo);
        DeleteObject(color);
        DeleteObject(mask);
        return icon == IntPtr.Zero ? LoadIcon(IntPtr.Zero, new IntPtr(32512)) : icon;
    }

    private static uint ColorRef(BatteryTone tone)
    {
        var (r, g, b) = tone switch
        {
            BatteryTone.Ok => ((byte)16, (byte)140, (byte)90),
            BatteryTone.Low => ((byte)186, (byte)120, (byte)20),
            BatteryTone.Critical => ((byte)176, (byte)52, (byte)44),
            _ => ((byte)72, (byte)72, (byte)72),
        };
        return (uint)(r | (g << 8) | (b << 16));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll")]
    private static extern bool Ellipse(IntPtr dc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr dc, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, int italic, int underline, int strike, int charset, int outPrecision, int clipPrecision, int quality, int pitch, string face);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr dc, string text, int count, ref RECT rect, uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
}
