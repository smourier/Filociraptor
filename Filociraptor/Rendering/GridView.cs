namespace Filociraptor.Rendering;

// the icon and thumbnail modes. same rule as the details view, only the cells on screen cost anything, and only they ever ask the shell for an image.
internal sealed class GridView : Control, IItemsView
{
    private const double _defaultSpacingPercent = 100;
    private const float _minCellWidth = 88;
    private const float _cellsPerWheelNotch = 1;
    private const float _selectionRadius = 4;

    // past this size a cell is big enough to be worth a real thumbnail, which is where Explorer switches too.
    private const float _thumbnailThreshold = 40;

    // how many lines a wrapped title is given, which is what its cell is made tall enough for.
    private const float _wrappedTitleLines = 2;

    private readonly Scrollbar _scrollbar = new();
    private float _scrollY;
    private int _hoverPosition = -1;
    private float _scale = 1;
    private int _columns = 1;
    private float _cellWidth = 1;
    private float _cellHeight = 1;

    public FolderItems? Items { get; set; }

    // what the user asked for, read every frame so a change shows without anything being rebuilt.
    public Settings? Settings { get; set; }
    public Action<int>? ItemActivated { get; set; }
    public int SelectedPosition { get; private set; } = -1;
    public ViewMode Mode { get; set; } = ViewMode.MediumIcons;

    public float ScrollOffset { get => _scrollY; set => _scrollY = Math.Max(0, value); }
    public int HoverPosition => _hoverPosition;
    public bool ScrollbarDragging => _scrollbar.Dragging;
    public int Columns => _columns;
    public int PageSize => Math.Max(1, VisibleRows - 1) * _columns;

    private int VisibleRows => (int)MathF.Ceiling(ListHeight / _cellHeight) + 1;
    private float ListHeight => MathF.Max(0, Bounds.bottom - Bounds.top);
    private int RowCount => Items == null ? 0 : (Items.Count + _columns - 1) / _columns;
    private float MaxScroll => MathF.Max(0, RowCount * _cellHeight - ListHeight);

    public static int IconSizeOf(ViewMode mode) => mode switch
    {
        ViewMode.SmallIcons => 32,
        ViewMode.MediumIcons => 48,
        ViewMode.LargeIcons => 96,
        ViewMode.Thumbnails => 192,
        _ => 16,
    };

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

    private void ScrollByWheel(int wheelDelta) =>
        _scrollY = Math.Clamp(_scrollY - wheelDelta / 120f * _cellsPerWheelNotch * _cellHeight, 0, MaxScroll);

    private bool SetHover(float x, float y)
    {
        var position = PositionAt(x, y);
        if (position == _hoverPosition)
            return false;

        _hoverPosition = position;
        return true;
    }

    public int PositionAtPoint(float x, float y) => PositionAt(x, y);

    public int PositionAt(float x, float y)
    {
        var items = Items;
        if (items == null || x < Bounds.left || x > Bounds.right - Scrollbar.Width * _scale || y < Bounds.top || y > Bounds.bottom)
            return -1;

        var column = (int)((x - Bounds.left) / _cellWidth);
        var row = (int)((y - Bounds.top + _scrollY) / _cellHeight);
        if (column < 0 || column >= _columns || row < 0)
            return -1;

        var position = row * _columns + column;
        return position < items.Count ? position : -1;
    }

    private bool BeginScrollbarDrag(float x, float y)
    {
        if (!_scrollbar.BeginDrag(x, y))
            return false;

        _scrollY = _scrollbar.ScrollFor(y);
        return true;
    }

    private void DragScrollbar(float y) => _scrollY = _scrollbar.ScrollFor(y);
    private void EndScrollbarDrag() => _scrollbar.EndDrag();

    private bool OnClick(float x, float y, bool doubleClick)
    {
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

    public void Select(int position)
    {
        var items = Items;
        if (items == null || items.Count == 0)
            return;

        SelectedPosition = Math.Clamp(position, 0, items.Count - 1);
        EnsureVisible(SelectedPosition);
    }

    public void MoveSelection(int delta) => Select(SelectedPosition < 0 ? 0 : SelectedPosition + delta);

    private void EnsureVisible(int position)
    {
        var row = position / _columns;
        var top = row * _cellHeight;
        if (top < _scrollY)
        {
            _scrollY = top;
            return;
        }

        var bottom = top + _cellHeight;
        if (bottom > _scrollY + ListHeight)
        {
            _scrollY = bottom - ListHeight;
        }
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, ImageCache images, string folderPath, bool streamItems)
    {
        _scale = resources.DpiScale;
        var iconSize = IconSizeOf(Mode) * _scale;

        // what the font asks for, taken as much or as little of as the user wants.
        var spacing = (Settings?.CellSpacingPercent ?? _defaultSpacingPercent) / _defaultSpacingPercent;
        var padding = MathF.Max(1, (float)(resources.CellSpacing * spacing));

        // a title can be off, one line, or two when it is allowed to wrap. the cell is measured from whichever it
        // is, so turning titles off gives the space back rather than leaving a gap where they were.
        var titleLines = Settings?.ThumbnailTitles == false ? 0 : Settings?.WrapThumbnailTitles == true ? _wrappedTitleLines : 1;
        var labelHeight = resources.LabelHeight * titleLines;

        _cellWidth = MathF.Max(_minCellWidth * _scale, iconSize + padding * 3);
        _cellHeight = iconSize + labelHeight + padding * (titleLines > 0 ? 3 : 2);

        var available = Bounds.right - Bounds.left - Scrollbar.Width * _scale;
        _columns = Math.Max(1, (int)(available / _cellWidth));
        _cellWidth = available / _columns;
        _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);

        var items = Items;
        if (items == null || items.Count == 0)
            return;

        deviceContext.PushAxisAlignedClip(Bounds, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        var wantThumbnail = Mode == ViewMode.Thumbnails || IconSizeOf(Mode) * resources.Zoom >= _thumbnailThreshold;
        var firstRow = Math.Max(0, (int)(_scrollY / _cellHeight));
        var lastRow = Math.Min(RowCount, firstRow + VisibleRows);

        for (var row = firstRow; row < lastRow; row++)
        {
            var y = Bounds.top - _scrollY + row * _cellHeight;
            for (var column = 0; column < _columns; column++)
            {
                var position = row * _columns + column;
                if (position >= items.Count)
                    break;

                var x = Bounds.left + column * _cellWidth;
                RenderCell(deviceContext, resources, images, folderPath, items, position, x, y, iconSize, padding, labelHeight, wantThumbnail, streamItems);
            }
        }

        deviceContext.PopAxisAlignedClip();
        _scrollbar.Update(Bounds, Bounds.top, _scrollY, MaxScroll, _scale);
        _scrollbar.Draw(deviceContext, resources);
    }

    private void RenderCell(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        ImageCache images,
        string folderPath,
        FolderItems items,
        int position,
        float x,
        float y,
        float iconSize,
        float padding,
        float labelHeight,
        bool wantThumbnail,
        bool streamItems)
    {
        var cell = new D2D_RECT_F { left = x, top = y, right = x + _cellWidth, bottom = y + _cellHeight };
        if (position == SelectedPosition || position == _hoverPosition)
        {
            var inset = new D2D_RECT_F
            {
                left = cell.left + 2 * _scale,
                top = cell.top + 2 * _scale,
                right = cell.right - 2 * _scale,
                bottom = cell.bottom - 2 * _scale,
            };

            var radius = _selectionRadius * _scale;
            var brush = position == SelectedPosition ? resources.SelectionBrush : resources.HoverBrush;
            deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = inset, radiusX = radius, radiusY = radius }, brush.Object);
        }

        ref readonly var entry = ref items.EntryAt(position);
        var name = items.NameOf(entry);
        var extension = items.ExtensionOf(entry);
        var image = images.GetOrRequest(name, extension, entry.IsDirectory, folderPath, (int)iconSize, wantThumbnail, items.ParsingNameOf(entry), isStream: streamItems);
        if (image != null)
        {
            var opacity = RenderResources.OpacityOf(entry);

            // only a real thumbnail is squared off. an icon is already square and cropping one would eat its edges.
            if (wantThumbnail && Settings?.SquareThumbnails == true && !entry.IsDirectory)
            {
                ImageDrawing.DrawSquare(deviceContext, image, x + _cellWidth / 2, y + padding + iconSize / 2, iconSize, opacity);
            }
            else
            {
                ImageDrawing.Draw(deviceContext, image, x + _cellWidth / 2, y + padding + iconSize / 2, iconSize, wantThumbnail, opacity);
            }
        }

        if (labelHeight <= 0)
            return;

        var labelRect = new D2D_RECT_F
        {
            left = cell.left + padding / 2,
            top = y + padding * 2 + iconSize,
            right = cell.right - padding / 2,
            bottom = y + padding * 2 + iconSize + labelHeight,
        };

        var brushForName = resources.NameBrush(entry, position == SelectedPosition);
        var format = Settings?.WrapThumbnailTitles == true ? resources.CenterWrapFormat : resources.CenterFormat;
        TextDrawing.Draw(deviceContext, name, format, labelRect, brushForName);
    }
}
