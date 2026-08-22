namespace Filociraptor.Rendering;

// a details listing that only ever touches the rows currently on screen.
// cost per frame is bounded by the height of the window, not by the number of files in the folder.
internal sealed class DetailsView : Control, IItemsView
{
    private const float _padding = 8;
    private const float _scrollbarWidth = Scrollbar.Width;
    private const float _iconSize = 16;
    private const float _rowsPerWheelNotch = 3;
    private const float _modifiedWidth = 132;
    private const float _typeWidth = 74;
    private const float _sizeWidth = 86;

    // the row height the column widths above were chosen against, so they scale with it.
    private const float _baseRowHeight = 22;

    private readonly Scrollbar _scrollbar = new();
    private float _scrollY;
    private int _hoverPosition = -1;

    // one per column, so a header lights up under the pointer the way the buttons in the caption do.
    private readonly HoverAnimation[] _headerHovers = new HoverAnimation[4];
    private int _hoverColumn = -1;

    public FolderItems? Items { get; set; }
    public int SelectedPosition { get; private set; } = -1;
    public float RowHeight { get; private set; } = 22;
    public Action<int>? ItemActivated { get; set; }
    public Action<SortColumn>? SortRequested { get; set; }

    public int VisibleRowCount => (int)MathF.Ceiling(ListHeight / RowHeight) + 1;
    public float ScrollOffset { get => _scrollY; set => SetScroll(value); }
    public int HoverPosition => _hoverPosition;
    public bool ScrollbarDragging => _scrollbar.Dragging;
    public int Columns => 1;
    public int PageSize => Math.Max(1, VisibleRowCount - 2);
    private float ListTop => Bounds.top + HeaderHeight;
    private float ListHeight => MathF.Max(0, Bounds.bottom - ListTop);
    private float HeaderHeight { get; set; } = 26;
    private float MaxScroll => MathF.Max(0, (Items?.Count ?? 0) * RowHeight - ListHeight);

    public void ScrollBy(float pixels) => SetScroll(_scrollY + pixels);
    public void ScrollByRows(float rows) => SetScroll(_scrollY + rows * RowHeight);
    private void ScrollByWheel(int wheelDelta) => SetScroll(_scrollY - wheelDelta / 120f * _rowsPerWheelNotch * RowHeight);

    private void SetScroll(float value) => _scrollY = Math.Clamp(value, 0, MaxScroll);

    public override bool IsCapturing => _scrollbar.Dragging;

    public override bool OnMouseMove(float x, float y)
    {
        if (_scrollbar.Dragging)
        {
            DragScrollbar(y);
            return true;
        }

        return SetHover(x, y);
    }

    public override bool OnMouseDown(float x, float y, bool doubleClick)
    {
        if (BeginScrollbarDrag(x, y))
            return true;

        return OnClick(x, y, doubleClick);
    }

    public override bool OnMouseUp()
    {
        if (!_scrollbar.Dragging)
            return false;

        EndScrollbarDrag();
        return true;
    }

    public override bool OnWheel(float x, float y, int delta)
    {
        if (!Contains(x, y))
            return false;

        ScrollByWheel(delta);
        return true;
    }

    public void Reset()
    {
        _scrollY = 0;
        SelectedPosition = -1;
        _hoverPosition = -1;
    }

    public void ClampScroll() => SetScroll(_scrollY);

    private bool InHeader(float x, float y) => y >= Bounds.top && y < ListTop && x >= Bounds.left && x < Bounds.right - _scrollbarWidth;

    private bool SetHover(float x, float y)
    {
        var column = InHeader(x, y) ? (int)ColumnAt(x) : -1;
        var position = PositionAt(x, y);
        if (position == _hoverPosition && column == _hoverColumn)
            return false;

        _hoverPosition = position;
        _hoverColumn = column;
        return true;
    }

    public int PositionAtPoint(float x, float y) => PositionAt(x, y);

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

    private bool BeginScrollbarDrag(float x, float y)
    {
        if (!_scrollbar.BeginDrag(x, y))
            return false;

        SetScroll(_scrollbar.ScrollFor(y));
        return true;
    }

    private void DragScrollbar(float y) => SetScroll(_scrollbar.ScrollFor(y));
    private void EndScrollbarDrag() => _scrollbar.EndDrag();

    private bool OnClick(float x, float y, bool doubleClick)
    {
        if (InHeader(x, y))
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
        var column = ColumnAt(x);

        SortRequested?.Invoke(column);
    }

    private SortColumn ColumnAt(float x)
    {
        var right = Bounds.right - _scrollbarWidth;
        var scale = RowHeight / _baseRowHeight;
        var sizeLeft = right - _sizeWidth * scale;
        var typeLeft = sizeLeft - _typeWidth * scale;
        var modifiedLeft = typeLeft - _modifiedWidth * scale;

        if (x >= sizeLeft)
            return SortColumn.Size;

        if (x >= typeLeft)
            return SortColumn.Type;

        if (x >= modifiedLeft)
            return SortColumn.Modified;

        return SortColumn.Name;
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, ImageCache images, string folderPath, bool streamItems)
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
        RenderRows(deviceContext, resources, images, folderPath, streamItems, modifiedLeft, typeLeft, sizeLeft, right, padding);
        _scrollbar.Update(Bounds, ListTop, _scrollY, MaxScroll, scale);
        _scrollbar.Draw(deviceContext, resources);
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

        // the highlight covers the whole cell, so it is drawn on the cell bounds rather than on the padded text.
        DrawHeaderHover(deviceContext, resources, SortColumn.Name, Bounds.left, modifiedLeft);
        DrawHeaderHover(deviceContext, resources, SortColumn.Modified, modifiedLeft, typeLeft);
        DrawHeaderHover(deviceContext, resources, SortColumn.Type, typeLeft, sizeLeft);
        DrawHeaderHover(deviceContext, resources, SortColumn.Size, sizeLeft, right);

        DrawHeaderCell(deviceContext, resources, Res.ColumnName, SortColumn.Name, Bounds.left + padding, modifiedLeft, false);
        DrawHeaderCell(deviceContext, resources, Res.ColumnModified, SortColumn.Modified, modifiedLeft, typeLeft, false);
        DrawHeaderCell(deviceContext, resources, Res.ColumnType, SortColumn.Type, typeLeft, sizeLeft, false);
        DrawHeaderCell(deviceContext, resources, Res.ColumnSize, SortColumn.Size, sizeLeft, right - padding, true);
    }

    private void DrawHeaderHover(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, SortColumn column, float left, float right)
    {
        ref var hover = ref _headerHovers[(int)column];
        if (hover.Advance(_hoverColumn == (int)column, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        var rect = new D2D_RECT_F { left = left, top = Bounds.top, right = right, bottom = ListTop };
        resources.FillHover(deviceContext, rect, hover.Opacity);
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
        ImageCache images,
        string folderPath,
        bool streamItems,
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
            var nameBrush = resources.NameBrush(entry, position == SelectedPosition);
            var detailBrush = position == SelectedPosition ? resources.TextBrush : resources.DimTextBrush;

            var name = items.NameOf(entry);
            var extension = items.ExtensionOf(entry);
            var iconSize = _iconSize * resources.DpiScale;
            var icon = images.GetOrRequest(name, extension, isDirectory, folderPath, (int)iconSize, false, items.ParsingNameOf(entry), isStream: streamItems);
            if (icon != null)
            {
                ImageDrawing.Draw(deviceContext, icon, Bounds.left + padding + iconSize / 2, y + RowHeight / 2, iconSize, false, RenderResources.OpacityOf(entry));
            }

            var nameLeft = Bounds.left + padding + iconSize + padding;
            var nameRect = new D2D_RECT_F { left = nameLeft, top = y, right = modifiedLeft - padding, bottom = rowRect.bottom };
            TextDrawing.Draw(deviceContext, name, resources.RowFormat, nameRect, nameBrush);

            var text = new ScratchText(buffer);
            if (entry.LastWriteTicks > 0)
            {
                text.AppendDateTime(new DateTime(entry.LastWriteTicks, DateTimeKind.Utc).ToLocalTime());
                var modifiedRect = new D2D_RECT_F { left = modifiedLeft, top = y, right = typeLeft - padding, bottom = rowRect.bottom };
                TextDrawing.Draw(deviceContext, text.Text, resources.RowFormat, modifiedRect, detailBrush);
            }

            // the shell hands the type name back with the icon, so this is the real one rather than the extension.
            var typeRect = new D2D_RECT_F { left = typeLeft, top = y, right = sizeLeft - padding, bottom = rowRect.bottom };
            var typeName = images.GetTypeNameFor(extension, isDirectory, (int)iconSize);
            if (typeName != null)
            {
                TextDrawing.Draw(deviceContext, typeName, resources.RowFormat, typeRect, detailBrush);
            }
            else
            {
                TextDrawing.Draw(deviceContext, isDirectory ? Res.Folder : extension, resources.RowFormat, typeRect, detailBrush);
            }

            if (!isDirectory && entry.Size > 0)
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
}
