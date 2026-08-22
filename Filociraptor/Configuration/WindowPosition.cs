namespace Filociraptor.Configuration;

internal struct WindowPosition
{
    private const int _parts = 9;
    private const char _separator = ';';

    public int OffsetX;
    public int OffsetY;
    public int Width;
    public int Height;
    public RECT MonitorBounds;
    public bool Maximized;

    public static unsafe WindowPosition? Get(Window? window)
    {
        if (window == null || window.Handle == 0)
            return null;

        var monitor = window.GetMonitor(MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor == null)
            return null;

        var rect = window.WindowRect;
        var work = monitor.WorkingArea;
        var left = rect.left;
        var top = rect.top;
        var width = rect.Width;
        var height = rect.Height;

        // a maximized window's own rect is the whole work area, which is not worth remembering. the size it would return to is in its placement instead.
        var maximized = window.IsZoomed;
        if (maximized)
        {
            var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            if (Functions.GetWindowPlacement(window.Handle, ref placement))
            {
                var normal = placement.rcNormalPosition;

                // rcNormalPosition is relative to the primary monitor's work area, the rest of this is in screen pixels, so it is shifted across.
                var primary = DirectN.Extensions.Utilities.Monitor.Primary;
                left = normal.left + (primary != null ? primary.WorkingArea.left : work.left);
                top = normal.top + (primary != null ? primary.WorkingArea.top : work.top);
                width = normal.right - normal.left;
                height = normal.bottom - normal.top;
            }
        }

        var dpi = DpiOf(monitor);
        return new WindowPosition
        {
            OffsetX = ToDips(left - work.left, dpi),
            OffsetY = ToDips(top - work.top, dpi),
            Width = ToDips(width, dpi),
            Height = ToDips(height, dpi),
            MonitorBounds = monitor.Bounds,
            Maximized = maximized,
        };
    }

    public readonly bool Restore(Window? window)
    {
        if (window == null || Width <= 0 || Height <= 0)
            return false;

        var monitor = FindBestMonitor();
        if (monitor == null)
            return false;

        var dpi = DpiOf(monitor);
        var work = monitor.WorkingArea;
        var width = Math.Min(ToPixels(Width, dpi), work.Width);
        var height = Math.Min(ToPixels(Height, dpi), work.Height);
        if (width <= 0 || height <= 0)
            return false;

        // put back where it was, then pulled inside the work area, which is what covers a screen that is now smaller than the one it was closed on.
        var x = Math.Clamp(work.left + ToPixels(OffsetX, dpi), work.left, Math.Max(work.left, work.right - width));
        var y = Math.Clamp(work.top + ToPixels(OffsetY, dpi), work.top, Math.Max(work.top, work.bottom - height));

        // the normal size is set first so the system records it as the restore size, and only then is it maximized, otherwise un-maximizing later would go somewhere arbitrary.
        window.ResizeAndMove(x, y, width, height);
        if (Maximized)
        {
            Functions.ShowWindow(window.Handle, SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);
        }

        return true;
    }

    private readonly DirectN.Extensions.Utilities.Monitor? FindBestMonitor()
    {
        DirectN.Extensions.Utilities.Monitor? best = null;
        var bestDistance = long.MaxValue;
        foreach (var monitor in DirectN.Extensions.Utilities.Monitor.All)
        {
            var bounds = monitor.Bounds;
            var size = Math.Abs(bounds.Width - MonitorBounds.Width) + Math.Abs(bounds.Height - MonitorBounds.Height);
            var offset = Math.Abs(bounds.left - MonitorBounds.left) + Math.Abs(bounds.top - MonitorBounds.top);
            var distance = (long)size * 1000 + offset;
            if (distance < bestDistance)
            {
                best = monitor;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static float DpiOf(DirectN.Extensions.Utilities.Monitor monitor)
    {
        var dpi = monitor.EffectiveDpi.width;
        return dpi > 0 ? dpi : Constants.USER_DEFAULT_SCREEN_DPI;
    }

    private static int ToDips(int pixels, float dpi) => (int)MathF.Round(pixels * Constants.USER_DEFAULT_SCREEN_DPI / dpi);
    private static int ToPixels(int dips, float dpi) => (int)MathF.Round(dips * dpi / Constants.USER_DEFAULT_SCREEN_DPI);

    public override readonly string ToString() => string.Join(_separator, OffsetX, OffsetY, Width, Height, Maximized ? 1 : 0, MonitorBounds.left, MonitorBounds.top, MonitorBounds.right, MonitorBounds.bottom);

    public static bool TryParse(string? text, out WindowPosition position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(_separator);
        if (parts.Length != _parts)
            return false;

        var numbers = new int[_parts];
        for (var i = 0; i < _parts; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]))
                return false;
        }

        position = new WindowPosition
        {
            OffsetX = numbers[0],
            OffsetY = numbers[1],
            Width = numbers[2],
            Height = numbers[3],
            Maximized = numbers[4] != 0,
            MonitorBounds = new RECT { left = numbers[5], top = numbers[6], right = numbers[7], bottom = numbers[8] },
        };

        return true;
    }
}
