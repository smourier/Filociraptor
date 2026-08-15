namespace Filociraptor.Rendering;

// a details listing that only ever touches the rows currently on screen.
// cost per frame is bounded by the height of the window, not by the number of files in the folder.
internal sealed class DetailsView
{
    private const float _padding = 8;
    private const float _scrollbarWidth = 11;
    private const float _minThumbHeight = 24;
    private const float _rowsPerWheelNotch = 3;
    private const float _modifiedWidth = 132;
    private const float _typeWidth = 74;
    private const float _sizeWidth = 86;

    private float _scrollY;
    private int _hoverPosition = -1;

    public FolderItems? Items { get; set; }
    public int SelectedPosition { get; private set; } = -1;
    public D2D_RECT_F Bounds { get; set; }
    public float RowHeight { get; private set; } = 22;
    public Action<int>? ItemActivated { get; set; }
    public Action<SortColumn>? SortRequested { get; set; }

    public int VisibleRowCount => (int)MathF.Ceiling(ListHeight / RowHeight) + 1;
    private float ListTop => Bounds.top + HeaderHeight;
    private float ListHeight => MathF.Max(0, Bounds.bottom - ListTop);
    private float HeaderHeight { get; set; } = 26;
    private float MaxScroll => MathF.Max(0, (Items?.Count ?? 0) * RowHeight - ListHeight);

    public void ScrollBy(float pixels) => SetScroll(_scrollY + pixels);
    public void ScrollByRows(float rows) => SetScroll(_scrollY + rows * RowHeight);
    public void ScrollByWheel(int wheelDelta) => SetScroll(_scrollY - wheelDelta / 120f * _rowsPerWheelNotch * RowHeight);

    private void SetScroll(float value) => _scrollY = Math.Clamp(value, 0, MaxScroll);

    public void Reset()
    {
        _scrollY = 0;
        SelectedPosition = -1;
        _hoverPosition = -1;
    }

    public void ClampScroll() => SetScroll(_scrollY);

    public bool SetHover(float x, float y)
    {
        var position = PositionAt(x, y);
        if (position == _hoverPosition)
            return false;

        _hoverPosition = position;
        return true;
    }

    public int PositionAt(float x, float y)
    {
        var items = Items;
        if (items == null || y < ListTop || y > Bounds.bottom || x < Bounds.left || x > Bounds.right - _scrollbarWidth)
            return -1;

        var position = (int)((y - ListTop + _scrollY) / RowHeight);
        return position >= 0 && position < items.Count ? position : -1;
    }

    public void Select(int position)
    {
        var items = Items;
        if (items == null || items.Count == 0)
            return;

        SelectedPosition = Math.Clamp(position, 0, items.Count - 1);
        EnsureVisible(SelectedPosition);
    }

    public void MoveSelection(int delta)
    {
        var items = Items;
        if (items == null || items.Count == 0)
            return;

        var position = SelectedPosition < 0 ? 0 : SelectedPosition + delta;
        Select(position);
    }

    public void EnsureVisible(int position)
    {
        var top = position * RowHeight;
        if (top < _scrollY)
        {
            SetScroll(top);
            return;
        }

        var bottom = top + RowHeight;
        if (bottom > _scrollY + ListHeight)
        {
            SetScroll(bottom - ListHeight);
        }
    }

    public bool OnClick(float x, float y, bool doubleClick)
    {
        if (y < ListTop && y >= Bounds.top)
        {
            OnHeaderClick(x);
            return true;
        }

        var position = PositionAt(x, y);
        if (position < 0)
            return false;

        SelectedPosition = position;
        if (doubleClick)
        {
            ItemActivated?.Invoke(position);
        }
        return true;
    }

    private void OnHeaderClick(float x)
    {
        var right = Bounds.right - _scrollbarWidth;
        var scale = RowHeight / 22;
        var sizeLeft = right - _sizeWidth * scale;
        var typeLeft = sizeLeft - _typeWidth * scale;
        var modifiedLeft = typeLeft - _modifiedWidth * scale;

        SortColumn column;
        if (x >= sizeLeft)
        {
            column = SortColumn.Size;
        }
        else if (x >= typeLeft)
        {
            column = SortColumn.Type;
        }
        else if (x >= modifiedLeft)
        {
            column = SortColumn.Modified;
        }
        else
        {
            column = SortColumn.Name;
        }

        SortRequested?.Invoke(column);
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        RowHeight = resources.RowHeight;
        HeaderHeight = resources.HeaderHeight;
        ClampScroll();

        var scale = resources.DpiScale;
        var right = Bounds.right - _scrollbarWidth * scale;
        var sizeLeft = right - _sizeWidth * scale;
        var typeLeft = sizeLeft - _typeWidth * scale;
        var modifiedLeft = typeLeft - _modifiedWidth * scale;
        var padding = _padding * scale;

        RenderHeader(deviceContext, resources, modifiedLeft, typeLeft, sizeLeft, right, padding);
        RenderRows(deviceContext, resources, modifiedLeft, typeLeft, sizeLeft, right, padding);
        RenderScrollbar(deviceContext, resources, scale);
    }

    private void RenderHeader(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        float modifiedLeft,
        float typeLeft,
        float sizeLeft,
        float right,
        float padding)
    {
        var header = new D2D_RECT_F { left = Bounds.left, top = Bounds.top, right = Bounds.right, bottom = ListTop };
        deviceContext.FillRectangle(header, resources.HeaderBackgroundBrush);
        deviceContext.DrawLine(
            new D2D_POINT_2F { x = Bounds.left, y = ListTop - 0.5f },
            new D2D_POINT_2F { x = Bounds.right, y = ListTop - 0.5f },
            resources.LineBrush);

        DrawHeaderCell(deviceContext, resources, "Name", SortColumn.Name, Bounds.left + padding, modifiedLeft, false);
        DrawHeaderCell(deviceContext, resources, "Modified", SortColumn.Modified, modifiedLeft, typeLeft, false);
        DrawHeaderCell(deviceContext, resources, "Type", SortColumn.Type, typeLeft, sizeLeft, false);
        DrawHeaderCell(deviceContext, resources, "Size", SortColumn.Size, sizeLeft, right - padding, true);
    }

    private void DrawHeaderCell(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        ReadOnlySpan<char> title,
        SortColumn column,
        float left,
        float right,
        bool alignRight)
    {
        Span<char> buffer = stackalloc char[32];
        var text = new ScratchText(buffer);
        var items = Items;
        if (items != null && items.SortColumn == column && items.Count > 0)
        {
            if (!alignRight)
            {
                text.Append(title);
                text.Append(items.SortDescending ? " ▾" : " ▴");
            }
            else
            {
                text.Append(items.SortDescending ? "▾ " : "▴ ");
                text.Append(title);
            }
        }
        else
        {
            text.Append(title);
        }

        var rect = new D2D_RECT_F { left = left, top = Bounds.top, right = right, bottom = ListTop };
        TextDrawing.Draw(deviceContext, text.Text, alignRight ? resources.RightFormat : resources.HeaderFormat, rect, resources.HeaderTextBrush);
    }

    private void RenderRows(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        float modifiedLeft,
        float typeLeft,
        float sizeLeft,
        float right,
        float padding)
    {
        var items = Items;
        if (items == null)
            return;

        var count = items.Count;
        if (count == 0)
            return;

        var listRect = new D2D_RECT_F { left = Bounds.left, top = ListTop, right = Bounds.right, bottom = Bounds.bottom };
        deviceContext.PushAxisAlignedClip(listRect, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        var first = Math.Max(0, (int)(_scrollY / RowHeight));
        var last = Math.Min(count, first + VisibleRowCount);
        var y = ListTop - _scrollY + first * RowHeight;

        Span<char> buffer = stackalloc char[64];
        for (var position = first; position < last; position++)
        {
            ref readonly var entry = ref items.EntryAt(position);
            var rowRect = new D2D_RECT_F { left = Bounds.left, top = y, right = Bounds.right, bottom = y + RowHeight };

            if (position == SelectedPosition)
            {
                deviceContext.FillRectangle(rowRect, resources.SelectionBrush);
            }
            else if (position == _hoverPosition)
            {
                deviceContext.FillRectangle(rowRect, resources.HoverBrush);
            }

            var isDirectory = entry.IsDirectory;
            var nameBrush = position == SelectedPosition ? resources.TextBrush : isDirectory ? resources.FolderTextBrush : resources.TextBrush;
            var detailBrush = position == SelectedPosition ? resources.TextBrush : resources.DimTextBrush;

            var nameRect = new D2D_RECT_F { left = Bounds.left + padding, top = y, right = modifiedLeft - padding, bottom = rowRect.bottom };
            TextDrawing.Draw(deviceContext, items.NameOf(entry), resources.RowFormat, nameRect, nameBrush);

            var text = new ScratchText(buffer);
            text.AppendDateTime(new DateTime(entry.LastWriteTicks, DateTimeKind.Utc).ToLocalTime());
            var modifiedRect = new D2D_RECT_F { left = modifiedLeft, top = y, right = typeLeft - padding, bottom = rowRect.bottom };
            TextDrawing.Draw(deviceContext, text.Text, resources.RowFormat, modifiedRect, detailBrush);

            var typeRect = new D2D_RECT_F { left = typeLeft, top = y, right = sizeLeft - padding, bottom = rowRect.bottom };
            if (isDirectory)
            {
                TextDrawing.Draw(deviceContext, "Folder", resources.RowFormat, typeRect, detailBrush);
            }
            else
            {
                TextDrawing.Draw(deviceContext, items.ExtensionOf(entry), resources.RowFormat, typeRect, detailBrush);
            }

            if (!isDirectory)
            {
                text.Clear();
                text.AppendSize(entry.Size);
                var sizeRect = new D2D_RECT_F { left = sizeLeft, top = y, right = right - padding, bottom = rowRect.bottom };
                TextDrawing.Draw(deviceContext, text.Text, resources.RightFormat, sizeRect, detailBrush);
            }

            y += RowHeight;
        }

        deviceContext.PopAxisAlignedClip();
    }

    private void RenderScrollbar(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, float scale)
    {
        var maxScroll = MaxScroll;
        if (maxScroll <= 0)
            return;

        var width = _scrollbarWidth * scale;
        var trackHeight = ListHeight;
        var total = trackHeight + maxScroll;
        var thumbHeight = MathF.Max(_minThumbHeight * scale, trackHeight * trackHeight / total);
        var thumbTop = ListTop + (trackHeight - thumbHeight) * (_scrollY / maxScroll);

        var thumb = new D2D_RECT_F
        {
            left = Bounds.right - width + 2 * scale,
            top = thumbTop,
            right = Bounds.right - 2 * scale,
            bottom = thumbTop + thumbHeight,
        };

        var radius = (thumb.right - thumb.left) / 2;
        var rounded = new D2D1_ROUNDED_RECT { rect = thumb, radiusX = radius, radiusY = radius };
        deviceContext.Object.FillRoundedRectangle(rounded, resources.ScrollbarBrush.Object);
    }
}
