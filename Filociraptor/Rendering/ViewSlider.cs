namespace Filociraptor.Rendering;

// one control for the whole range of listing sizes, from the details rows through the icon sizes up to thumbnails.
// the keyboard shortcuts set the same five stops, so there is only ever one idea of the current view.
internal sealed class ViewSlider
{
    private const int _stopCount = 5;
    private const float _trackHeight = 3;
    private const float _thumbRadius = 6;
    private const float _tickRadius = 1.5f;
    private const float _inset = 10;

    private float _scale = 1;

    public D2D_RECT_F Bounds { get; private set; }
    public ViewMode Mode { get; set; }
    public bool Dragging { get; private set; }
    public bool Hot { get; private set; }
    public Action<ViewMode>? ModeChanged { get; set; }

    private float TrackLeft => Bounds.left + _inset * _scale;
    private float TrackRight => Bounds.right - _inset * _scale;

    public void Update(in D2D_RECT_F bounds, float scale)
    {
        _scale = scale;
        Bounds = bounds;
    }

    public bool Contains(float x, float y) => x >= Bounds.left && x < Bounds.right && y >= Bounds.top && y < Bounds.bottom;

    public bool SetHover(float x, float y)
    {
        var hot = Contains(x, y);
        if (hot == Hot)
            return false;

        Hot = hot;
        return true;
    }

    public bool BeginDrag(float x, float y)
    {
        if (!Contains(x, y))
            return false;

        Dragging = true;
        Drag(x);
        return true;
    }

    public void Drag(float x)
    {
        var travel = TrackRight - TrackLeft;
        if (travel <= 0)
            return;

        var stop = (int)MathF.Round((x - TrackLeft) / travel * (_stopCount - 1));
        var mode = (ViewMode)Math.Clamp(stop, 0, _stopCount - 1);
        if (mode == Mode)
            return;

        Mode = mode;
        ModeChanged?.Invoke(mode);
    }

    public void EndDrag() => Dragging = false;

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        var centreY = MathF.Round((Bounds.top + Bounds.bottom) / 2);
        var height = _trackHeight * _scale;
        var track = new D2D_RECT_F
        {
            left = TrackLeft,
            top = centreY - height / 2,
            right = TrackRight,
            bottom = centreY + height / 2,
        };

        var radius = height / 2;
        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = track, radiusX = radius, radiusY = radius }, resources.BarTrackBrush.Object);

        var travel = TrackRight - TrackLeft;
        for (var i = 0; i < _stopCount; i++)
        {
            var x = TrackLeft + travel * i / (_stopCount - 1);
            var tick = new D2D1_ELLIPSE { point = new D2D_POINT_2F { x = x, y = centreY }, radiusX = _tickRadius * _scale, radiusY = _tickRadius * _scale };
            deviceContext.Object.FillEllipse(tick, resources.ScrollbarBrush.Object);
        }

        var thumbX = TrackLeft + travel * (int)Mode / (_stopCount - 1);
        var thumbRadius = _thumbRadius * _scale;
        var thumb = new D2D1_ELLIPSE { point = new D2D_POINT_2F { x = thumbX, y = centreY }, radiusX = thumbRadius, radiusY = thumbRadius };
        deviceContext.Object.FillEllipse(thumb, Dragging || Hot ? resources.SplitterHotBrush.Object : resources.TextBrush.Object);
    }
}
