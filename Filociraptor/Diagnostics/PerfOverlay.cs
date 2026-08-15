namespace Filociraptor.Diagnostics;

internal sealed class PerfOverlay
{
    private const float _width = 268;
    private const float _lineHeight = 17;
    private const float _graphHeight = 46;
    private const float _padding = 10;
    private const float _budgetMilliseconds = 16.67f;
    private const int _lineCount = 8;

    public bool Visible { get; set; }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, PerfCounters counters, in D2D_RECT_F bounds)
    {
        if (!Visible)
            return;

        var scale = resources.DpiScale;
        var width = _width * scale;
        var lineHeight = _lineHeight * scale;
        var padding = _padding * scale;
        var graphHeight = _graphHeight * scale;
        var height = padding * 2 + lineHeight * _lineCount + graphHeight;

        var panel = new D2D_RECT_F
        {
            left = bounds.right - width - padding,
            top = bounds.top + padding,
            right = bounds.right - padding,
            bottom = bounds.top + padding + height,
        };

        var rounded = new D2D1_ROUNDED_RECT { rect = panel, radiusX = 6 * scale, radiusY = 6 * scale };
        deviceContext.Object.FillRoundedRectangle(rounded, resources.OverlayBackgroundBrush.Object);

        var y = panel.top + padding;
        var left = panel.left + padding;
        var right = panel.right - padding;

        Span<char> buffer = stackalloc char[96];

        var text = new ScratchText(buffer);
        text.Append("items      ");
        text.Append(counters.ItemCount);
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        text.Clear();
        text.Append("scan       ");
        text.Append(counters.ScanMilliseconds, "F1");
        text.Append(" ms");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        text.Clear();
        text.Append("first rows ");
        text.Append(counters.FirstRowsMilliseconds, "F1");
        text.Append(" ms");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        text.Clear();
        text.Append("sort       ");
        text.Append(counters.SortMilliseconds, "F1");
        text.Append(" ms");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        var average = counters.AverageFrameMilliseconds;
        text.Clear();
        text.Append("frame      ");
        text.Append(counters.LastFrameMilliseconds, "F2");
        text.Append(" ms, max ");
        text.Append(counters.MaxFrameMilliseconds, "F2");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, average <= _budgetMilliseconds ? resources.GoodBrush : resources.BadBrush);
        y += lineHeight;

        text.Clear();
        text.Append("buffers    ");
        text.Append(counters.BufferBytes / (1024 * 1024));
        text.Append(" MB, working set ");
        text.Append(Environment.WorkingSet / (1024 * 1024));
        text.Append(" MB");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        text.Clear();
        text.Append("scan alloc ");
        text.Append(counters.ScanAllocatedBytes / 1024);
        text.Append(" KB");
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        text.Clear();
        text.Append("gc         ");
        text.Append(GC.CollectionCount(0));
        text.Append(" / ");
        text.Append(GC.CollectionCount(1));
        text.Append(" / ");
        text.Append(GC.CollectionCount(2));
        DrawLine(deviceContext, resources, text.Text, left, right, y, lineHeight, resources.OverlayTextBrush);
        y += lineHeight;

        RenderGraph(deviceContext, resources, counters, left, right, y, graphHeight);
    }

    private static void DrawLine(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        ReadOnlySpan<char> text,
        float left,
        float right,
        float top,
        float height,
        IComObject<ID2D1Brush> brush)
    {
        var rect = new D2D_RECT_F { left = left, top = top, right = right, bottom = top + height };
        TextDrawing.Draw(deviceContext, text, resources.OverlayFormat, rect, brush);
    }

    // one bar per recent frame, with the 60 Hz budget drawn across as a reference.
    private static void RenderGraph(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        PerfCounters counters,
        float left,
        float right,
        float top,
        float height)
    {
        var samples = counters.Samples;
        var width = right - left;
        var barWidth = width / PerfCounters.SampleCount;
        var maximum = MathF.Max(_budgetMilliseconds * 2, (float)counters.MaxFrameMilliseconds);
        var bottom = top + height;

        for (var i = 0; i < PerfCounters.SampleCount; i++)
        {
            var index = (counters.SampleIndex + i) % PerfCounters.SampleCount;
            var value = (float)samples[index];
            if (value <= 0)
                continue;

            var barHeight = MathF.Min(height, value / maximum * height);
            var bar = new D2D_RECT_F
            {
                left = left + i * barWidth,
                top = bottom - barHeight,
                right = left + (i + 1) * barWidth,
                bottom = bottom,
            };
            deviceContext.FillRectangle(bar, value <= _budgetMilliseconds ? resources.GoodBrush : resources.BadBrush);
        }

        var budgetY = bottom - _budgetMilliseconds / maximum * height;
        deviceContext.DrawLine(
            new D2D_POINT_2F { x = left, y = budgetY },
            new D2D_POINT_2F { x = right, y = budgetY },
            resources.LineBrush);
    }
}
