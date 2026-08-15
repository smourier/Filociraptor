namespace Filociraptor.Rendering;

// geometry, drawing and dragging for one vertical scrollbar.
// owned by the view it belongs to, because hit testing needs exactly the same numbers the drawing used.
internal sealed class Scrollbar
{
    public const float Width = 11;
    private const float _minThumbHeight = 24;
    private const float _inset = 2;

    private D2D_RECT_F _thumb;
    private float _left;
    private float _trackTop;
    private float _trackHeight;
    private float _maxScroll;
    private float _grabOffset;

    public bool Dragging { get; private set; }
    public bool Visible => _maxScroll > 0 && _trackHeight > 0;

    public void Update(in D2D_RECT_F bounds, float trackTop, float scrollY, float maxScroll, float scale)
    {
        _left = bounds.right - Width * scale;
        _trackTop = trackTop;
        _trackHeight = MathF.Max(0, bounds.bottom - trackTop);
        _maxScroll = maxScroll;
        if (!Visible)
            return;

        var height = MathF.Max(_minThumbHeight * scale, _trackHeight * _trackHeight / (_trackHeight + maxScroll));
        var top = _trackTop + (_trackHeight - height) * (scrollY / maxScroll);
        _thumb = new D2D_RECT_F
        {
            left = _left + _inset * scale,
            top = top,
            right = bounds.right - _inset * scale,
            bottom = top + height,
        };
    }

    public void Draw(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        if (!Visible)
            return;

        var radius = (_thumb.right - _thumb.left) / 2;
        var brush = Dragging ? resources.SplitterHotBrush : resources.ScrollbarBrush;
        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = _thumb, radiusX = radius, radiusY = radius }, brush.Object);
    }

    public bool Contains(float x, float y) => Visible && x >= _left && y >= _trackTop && y <= _trackTop + _trackHeight;

    // pressing the track jumps the thumb to the pointer and keeps dragging from there, which is what Windows itself now does, rather than paging.
    public bool BeginDrag(float x, float y)
    {
        if (!Contains(x, y))
            return false;

        var height = _thumb.bottom - _thumb.top;
        _grabOffset = y >= _thumb.top && y <= _thumb.bottom ? y - _thumb.top : height / 2;
        Dragging = true;
        return true;
    }

    public void EndDrag() => Dragging = false;

    public float ScrollFor(float y)
    {
        var height = _thumb.bottom - _thumb.top;
        var travel = _trackHeight - height;
        if (travel <= 0)
            return 0;

        return Math.Clamp((y - _grabOffset - _trackTop) / travel * _maxScroll, 0, _maxScroll);
    }
}
