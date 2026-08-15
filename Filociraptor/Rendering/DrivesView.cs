namespace Filociraptor.Rendering;

internal sealed class DrivesView
{
    private const float _padding = 10;
    private const float _rowHeight = 54;
    private const float _headerHeight = 26;
    private const float _barHeight = 5;
    private const float _lineHeight = 18;
    private const float _lowSpaceRatio = 0.9f;
    private const float _wheelRows = 2;

    private readonly List<DriveEntry> _drives = [];
    private float _scrollY;
    private int _hoverIndex = -1;

    public int SelectedIndex { get; private set; } = -1;
    public D2D_RECT_F Bounds { get; set; }
    public Action<DriveEntry>? DriveActivated { get; set; }
    public IReadOnlyList<DriveEntry> Drives => _drives;

    private float Scale { get; set; } = 1;
    private float ListTop => Bounds.top + _headerHeight * Scale;
    private float ListHeight => MathF.Max(0, Bounds.bottom - ListTop);
    private float MaxScroll => MathF.Max(0, _drives.Count * _rowHeight * Scale - ListHeight);

    public void Clear()
    {
        _drives.Clear();
        SelectedIndex = -1;
        _scrollY = 0;
    }

    public void Add(DriveEntry drive) => _drives.Add(drive);

    public void ScrollByWheel(int wheelDelta) =>
        _scrollY = Math.Clamp(_scrollY - wheelDelta / 120f * _wheelRows * _rowHeight * Scale, 0, MaxScroll);

    // marks whichever drive the given path lives on, so the pane follows navigation done from the listing.
    public void SyncTo(string path)
    {
        for (var i = 0; i < _drives.Count; i++)
        {
            if (path.StartsWith(_drives[i].Root, StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = i;
                return;
            }
        }

        SelectedIndex = -1;
    }

    public bool SetHover(float x, float y)
    {
        var index = IndexAt(x, y);
        if (index == _hoverIndex)
            return false;

        _hoverIndex = index;
        return true;
    }

    public int IndexAt(float x, float y)
    {
        if (x < Bounds.left || x > Bounds.right || y < ListTop || y > Bounds.bottom)
            return -1;

        var index = (int)((y - ListTop + _scrollY) / (_rowHeight * Scale));
        return index >= 0 && index < _drives.Count ? index : -1;
    }

    public bool OnClick(float x, float y)
    {
        var index = IndexAt(x, y);
        if (index < 0)
            return false;

        SelectedIndex = index;
        DriveActivated?.Invoke(_drives[index]);
        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        Scale = resources.DpiScale;
        _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);

        deviceContext.FillRectangle(Bounds, resources.PaneBackgroundBrush);

        var padding = _padding * Scale;
        var header = new D2D_RECT_F { left = Bounds.left + padding, top = Bounds.top, right = Bounds.right, bottom = ListTop };
        TextDrawing.Draw(deviceContext, Res.Drives, resources.HeaderFormat, header, resources.HeaderTextBrush);

        var listRect = new D2D_RECT_F { left = Bounds.left, top = ListTop, right = Bounds.right, bottom = Bounds.bottom };
        deviceContext.PushAxisAlignedClip(listRect, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        var rowHeight = _rowHeight * Scale;
        var y = ListTop - _scrollY;

        Span<char> buffer = stackalloc char[96];
        for (var i = 0; i < _drives.Count; i++)
        {
            if (y + rowHeight >= ListTop && y <= Bounds.bottom)
            {
                RenderDrive(deviceContext, resources, _drives[i], i, y, rowHeight, padding, buffer);
            }

            y += rowHeight;
        }

        deviceContext.PopAxisAlignedClip();
    }

    private void RenderDrive(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        DriveEntry drive,
        int index,
        float y,
        float rowHeight,
        float padding,
        Span<char> buffer)
    {
        var row = new D2D_RECT_F { left = Bounds.left, top = y, right = Bounds.right, bottom = y + rowHeight };
        if (index == SelectedIndex)
        {
            deviceContext.FillRectangle(row, resources.SelectionBrush);
        }
        else if (index == _hoverIndex)
        {
            deviceContext.FillRectangle(row, resources.HoverBrush);
        }

        var left = Bounds.left + padding;
        var right = Bounds.right - padding;
        var lineHeight = _lineHeight * Scale;

        var text = new ScratchText(buffer);
        text.Append(drive.Label.Length > 0 ? drive.Label : drive.TypeName);
        text.Append(" (");
        text.Append(drive.Root.AsSpan().TrimEnd('\\'));
        text.Append(')');

        var titleRect = new D2D_RECT_F { left = left, top = y + padding / 2, right = right, bottom = y + padding / 2 + lineHeight };
        TextDrawing.Draw(deviceContext, text.Text, resources.RowFormat, titleRect, resources.TextBrush);

        if (!drive.IsReady || drive.TotalBytes == 0)
        {
            var emptyRect = new D2D_RECT_F { left = left, top = titleRect.bottom, right = right, bottom = row.bottom };
            TextDrawing.Draw(deviceContext, drive.IsReady ? Res.DriveUnknownSize : Res.DriveNotReady, resources.RowFormat, emptyRect, resources.DimTextBrush);
            return;
        }

        var barTop = titleRect.bottom + 3 * Scale;
        var barHeight = _barHeight * Scale;
        var track = new D2D_RECT_F { left = left, top = barTop, right = right, bottom = barTop + barHeight };
        var radius = barHeight / 2;
        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = track, radiusX = radius, radiusY = radius }, resources.BarTrackBrush.Object);

        var used = track;
        used.right = track.left + (track.right - track.left) * drive.UsedRatio;
        if (used.right > used.left)
        {
            var fill = drive.UsedRatio >= _lowSpaceRatio ? resources.BarFillLowBrush : resources.BarFillBrush;
            deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = used, radiusX = radius, radiusY = radius }, fill.Object);
        }

        text.Clear();
        text.AppendSize(drive.FreeBytes);
        text.Append(Res.DriveFreeOf);
        text.AppendSize(drive.TotalBytes);

        var freeRect = new D2D_RECT_F { left = left, top = track.bottom, right = right, bottom = row.bottom };
        TextDrawing.Draw(deviceContext, text.Text, resources.RowFormat, freeRect, resources.DimTextBrush);
    }
}
