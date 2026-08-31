namespace Filociraptor.Rendering;

// the left pane, drives first and then the places the shell offers, which is what Explorer lists in its tree.
internal sealed class PlacesView : Control
{
    private const float _padding = 10;
    private const float _driveHeight = 54;
    private const float _placeHeight = 28;
    private const float _separatorGap = 8;
    private const float _barHeight = 5;
    private const float _lineHeight = 18;
    private const float _iconSize = 16;
    private const float _lowSpaceRatio = 0.9f;
    private const float _wheelRows = 2;

    private readonly List<DriveEntry> _drives = [];
    private readonly List<PlaceEntry> _places = [];

    private RowHover _hover = new();
    private float _scrollY;

    public int SelectedIndex { get; private set; } = -1;
    public Action<DriveEntry>? DriveActivated { get; set; }
    public Action<PlaceEntry>? PlaceActivated { get; set; }
    public IReadOnlyList<DriveEntry> Drives => _drives;

    private float ListTop => Bounds.top;
    private float ListHeight => MathF.Max(0, Bounds.bottom - ListTop);
    private float SeparatorGap => _places.Count > 0 && _drives.Count > 0 ? _separatorGap * Scale : 0;
    private float ContentHeight => (_drives.Count * _driveHeight + _places.Count * _placeHeight) * Scale + SeparatorGap;
    private float MaxScroll => MathF.Max(0, ContentHeight - ListHeight);

    public void Clear()
    {
        _drives.Clear();
        SelectedIndex = -1;
        _scrollY = 0;
    }

    public void Add(DriveEntry drive)
    {
        var existing = _drives.FindIndex(d => d.Root.EqualsIgnoreCase(drive.Root));
        if (existing >= 0)
        {
            _drives[existing] = drive;
            return;
        }

        var index = _drives.FindIndex(d => string.Compare(d.Root, drive.Root, StringComparison.OrdinalIgnoreCase) > 0);
        if (index < 0)
        {
            _drives.Add(drive);
            return;
        }

        _drives.Insert(index, drive);
    }

    public void SetPlaces(IReadOnlyList<PlaceEntry> places)
    {
        _places.Clear();
        _places.AddRange(places);
    }

    public override bool OnWheel(float x, float y, int delta)
    {
        if (!Contains(x, y))
            return false;

        _scrollY = Math.Clamp(_scrollY - delta / 120f * _wheelRows * _driveHeight * Scale, 0, MaxScroll);
        return true;
    }

    // marks whichever row the current location belongs to, so the pane follows navigation done from the listing.
    public void SyncTo(ShellLocation location)
    {
        var path = location.Path ?? string.Empty;
        if (path.Length > 0)
        {
            for (var i = 0; i < _drives.Count; i++)
            {
                if (path.StartsWith(_drives[i].Root, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }

        for (var i = 0; i < _places.Count; i++)
        {
            if (string.Equals(location.ParsingName, _places[i].ParsingName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = _drives.Count + i;
                return;
            }
        }

        SelectedIndex = -1;
    }

    public override bool OnMouseMove(float x, float y) => _hover.MoveTo(IndexAt(x, y));

    private int RowCount => _drives.Count + _places.Count;
    private float HeightOf(int index) => (index < _drives.Count ? _driveHeight : _placeHeight) * Scale;

    private float TopOf(int index) => (Math.Min(index, _drives.Count) * _driveHeight + Math.Max(0, index - _drives.Count) * _placeHeight) * Scale + (index >= _drives.Count ? SeparatorGap : 0);

    private float DrivesBottom => _drives.Count * _driveHeight * Scale;

    public int IndexAt(float x, float y)
    {
        if (x < Bounds.left || x > Bounds.right || y < ListTop || y > Bounds.bottom)
            return -1;

        var target = y - ListTop + _scrollY;
        for (var i = 0; i < RowCount; i++)
        {
            var top = TopOf(i);
            if (target >= top && target < top + HeightOf(i))
                return i;
        }

        return -1;
    }

    public override bool OnMouseDown(float x, float y, bool doubleClick)
    {
        var index = IndexAt(x, y);
        if (index < 0)
            return false;

        SelectedIndex = index;
        if (index < _drives.Count)
        {
            DriveActivated?.Invoke(_drives[index]);
        }
        else
        {
            PlaceActivated?.Invoke(_places[index - _drives.Count]);
        }

        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, ImageCache? images)
    {
        Scale = resources.DpiScale;
        _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);

        deviceContext.FillRectangle(Bounds, resources.PaneBackgroundBrush);
        deviceContext.PushAxisAlignedClip(Bounds, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        if (_hover.Advance(resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        var padding = _padding * Scale;
        var origin = ListTop - _scrollY;

        if (SeparatorGap > 0)
        {
            // in the gap between the two halves, which TopOf accounts for.
            var middle = MathF.Round(origin + DrivesBottom + SeparatorGap / 2) - 0.5f;
            deviceContext.DrawLine(new D2D_POINT_2F { x = Bounds.left + padding, y = middle }, new D2D_POINT_2F { x = Bounds.right - padding, y = middle }, resources.LineBrush);
        }

        Span<char> buffer = stackalloc char[96];
        for (var i = 0; i < RowCount; i++)
        {
            var y = origin + TopOf(i);
            var height = HeightOf(i);
            if (y + height < ListTop || y > Bounds.bottom)
                continue;

            if (i < _drives.Count)
            {
                RenderDrive(deviceContext, resources, _drives[i], i, y, height, padding, buffer);
            }
            else
            {
                RenderPlace(deviceContext, resources, images, _places[i - _drives.Count], i, y, height, padding);
            }
        }

        deviceContext.PopAxisAlignedClip();
    }

    private void DrawRowBackground(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, int index, in D2D_RECT_F row)
    {
        if (index == SelectedIndex)
        {
            deviceContext.FillRectangle(row, resources.SelectionBrush);
            return;
        }

        resources.FillHover(deviceContext, row, _hover.OpacityOf(index));
    }

    private void RenderPlace(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        ImageCache? images,
        PlaceEntry place,
        int index,
        float y,
        float rowHeight,
        float padding)
    {
        var row = new D2D_RECT_F { left = Bounds.left, top = y, right = Bounds.right, bottom = y + rowHeight };
        DrawRowBackground(deviceContext, resources, index, row);

        var left = Bounds.left + padding;
        var iconSize = MathF.Round(_iconSize * Scale);
        if (images != null)
        {
            // keyed by parsing name, the way any other namespace item is.
            var bitmap = images.GetOrRequest(place.DisplayName, default, true, string.Empty, (int)iconSize, false, place.ParsingName, keep: true);
            if (bitmap != null)
            {
                ImageDrawing.Draw(deviceContext, bitmap, left + iconSize / 2, y + rowHeight / 2, iconSize, false, 1);
            }
        }

        var textRect = new D2D_RECT_F
        {
            left = left + iconSize + padding / 2,
            top = y,
            right = Bounds.right - padding,
            bottom = row.bottom,
        };

        TextDrawing.Draw(deviceContext, place.DisplayName, resources.RowFormat, textRect, resources.TextBrush);
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
        DrawRowBackground(deviceContext, resources, index, row);

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

        // nothing is said about the room on it until it has been asked,
        // because saying "not ready" first and correcting it a moment later is worse than saying nothing.
        if (drive.IsPending)
            return;

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
