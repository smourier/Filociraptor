namespace Filociraptor.Rendering;

// the menu behind the gear.
// it is drawn like everything else here rather than being a real popup window, so it costs no window, no message loop of its own and no theming, and it follows the same zoom and the same colours as the listing.
internal sealed class SettingsMenu : Control
{
    private const float _rowHeight = 26;
    private const float _separatorHeight = 7;
    private const float _padding = 10;
    private const float _gap = 18;
    private const float _minWidth = 210;
    private const float _maxWidth = 460;
    private const float _sliderWidth = 90;
    private const float _sliderHeight = 3;
    private const float _thumbRadius = 5;
    private const float _swatchWidth = 26;
    private const float _swatchHeight = 12;
    private const float _shadow = 1;
    private const float _wheelRows = 3;


    private IReadOnlyList<MenuEntry> _entries = [];
    private IReadOnlyList<MenuEntry> _children = [];
    private MenuEntry? _openParent;
    private RowHover _hover = new();
    private RowHover _childHover = new();
    private MenuEntry? _dragging;
    private float _averageCharacter = 7;

    // a list can be longer than the window, the installed fonts being the obvious one, so each of the two lists scrolls on its own and the wheel goes to whichever one the pointer is over.
    private float _scrollY;
    private float _childScrollY;
    private D2D_RECT_F _frame;

    public bool IsOpen { get; private set; }
    public D2D_RECT_F ChildBounds { get; private set; }
    public Action? Changed { get; set; }
    public Action? Closed { get; set; }

    // it takes input only while it is up, and it takes all of it, being over everything else.
    public override bool IsInteractive => IsOpen;
    public override bool IsModal => IsOpen;
    public override bool IsCapturing => _dragging != null;
    public override bool Contains(float x, float y) => base.Contains(x, y) || (_children.Count > 0 && x >= ChildBounds.left && x < ChildBounds.right && y >= ChildBounds.top && y < ChildBounds.bottom);

    // the anchor is the control the menu hangs from, the gear, and the frame is what it must stay inside.
    public void Open(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F anchor, in D2D_RECT_F frame, RenderResources resources)
    {
        _entries = entries;
        Scale = resources.ChromeScale;
        _averageCharacter = resources.CaptionCharacterWidth;
        var scale = Scale;
        _frame = frame;
        _scrollY = 0;
        IsOpen = true;
        CloseSubmenu();
        _hover.Reset();

        var width = Math.Clamp(WidthOf(entries), _minWidth * scale, _maxWidth * scale);
        var height = HeightOf(entries);

        // right aligned under the gear, and pulled back inside the window when there is not room for it.
        var left = MathF.Min(anchor.right - width, frame.right - width - _padding * scale);
        left = MathF.Max(left, frame.left + _padding * scale);
        var top = anchor.bottom;
        Bounds = new D2D_RECT_F { left = left, top = top, right = left + width, bottom = MathF.Min(top + height, frame.bottom) };
    }

    private float MaxScrollOf(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F bounds) => MathF.Max(0, HeightOf(entries) - (bounds.bottom - bounds.top));

    // true when it took the wheel, so the listing underneath does not scroll as well.
    public override bool OnWheel(float x, float y, int delta)
    {
        var notches = delta / 120f * _wheelRows * _rowHeight * Scale;
        if (_children.Count > 0 && x >= ChildBounds.left && x < ChildBounds.right && y >= ChildBounds.top && y < ChildBounds.bottom)
        {
            _childScrollY = Math.Clamp(_childScrollY - notches, 0, MaxScrollOf(_children, ChildBounds));
            _childHover.MoveTo(IndexAt(_children, ChildBounds, x, y, out _));
            return true;
        }

        if (x >= Bounds.left && x < Bounds.right && y >= Bounds.top && y < Bounds.bottom)
        {
            _scrollY = Math.Clamp(_scrollY - notches, 0, MaxScrollOf(_entries, Bounds));
            _hover.MoveTo(IndexAt(_entries, Bounds, x, y, out _));
            return true;
        }

        return false;
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        CloseSubmenu();
        _dragging = null;
        _hover.Reset();
        Closed?.Invoke();
    }

    private void CloseSubmenu()
    {
        _openParent = null;
        _children = [];
        _childHover.Reset();
        _childScrollY = 0;
        ChildBounds = default;
    }

    // what a row keeps to the right of its label, its value and whatever furniture it carries.
    private float ReserveOf(MenuEntry entry)
    {
        var value = (entry.Value?.Invoke().Length ?? 0) * _averageCharacter;
        return entry.Kind switch
        {
            MenuEntryKind.Slider => (_sliderWidth + _gap) * Scale + value,
            MenuEntryKind.Color => (_swatchWidth + _gap) * Scale,
            MenuEntryKind.Choice or MenuEntryKind.Submenu => _gap * Scale + value,
            _ => value,
        };
    }

    private float WidthOf(IReadOnlyList<MenuEntry> entries)
    {
        // measured from the average character width rather than laid out, a menu is a dozen short strings and laying each one out would cost more than it is worth.
        var widest = 0f;
        foreach (var entry in entries)
        {
            // a character of slack, the widths here are an average and a label of wide letters measures a little short of what it draws, which is enough to have it touch its value.
            var width = (entry.Label.Length + 1) * _averageCharacter + ReserveOf(entry);
            if (width > widest)
            {
                widest = width;
            }
        }

        return widest + _padding * 2 * Scale;
    }

    private float HeightOf(IReadOnlyList<MenuEntry> entries)
    {
        var height = _padding * Scale * 2;
        foreach (var entry in entries)
        {
            height += entry.Kind == MenuEntryKind.Separator ? _separatorHeight * Scale : _rowHeight * Scale;
        }

        return height;
    }

    private int IndexAt(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F bounds, float x, float y, out D2D_RECT_F row)
    {
        row = default;
        if (x < bounds.left || x >= bounds.right || y < bounds.top || y >= bounds.bottom)
            return -1;

        var top = bounds.top + _padding * Scale - ScrollOf(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            var height = (entries[i].Kind == MenuEntryKind.Separator ? _separatorHeight : _rowHeight) * Scale;
            if (y >= top && y < top + height)
            {
                row = new D2D_RECT_F { left = bounds.left, top = top, right = bounds.right, bottom = top + height };
                return entries[i].IsInteractive ? i : -1;
            }

            top += height;
        }

        return -1;
    }

    public override bool OnMouseMove(float x, float y)
    {
        if (_dragging != null)
        {
            // the row the slider lives on is wherever it was when the drag began, only x matters after that.
            DragTo(_dragging, x, default);
            Changed?.Invoke();
            return true;
        }

        var child = _children.Count > 0 ? IndexAt(_children, ChildBounds, x, y, out _) : -1;
        var index = child >= 0 ? _hover.Index : IndexAt(_entries, Bounds, x, y, out _);
        var changed = _hover.MoveTo(index) | _childHover.MoveTo(child);

        // moving onto another row of the menu closes whatever submenu was open, the way a menu behaves.
        if (child < 0 && index >= 0 && !ReferenceEquals(_entries[index], _openParent))
        {
            var entry = _entries[index];
            if (entry.Kind is MenuEntryKind.Choice or MenuEntryKind.Submenu)
            {
                OpenSubmenu(entry, index);
            }
            else
            {
                CloseSubmenu();
            }
        }

        // the menu is over everything while it is up, so it swallows the message either way.
        return changed || Contains(x, y);
    }

    private void OpenSubmenu(MenuEntry entry, int index)
    {
        var children = entry.Children?.Invoke() ?? [];
        if (children.Count == 0)
        {
            CloseSubmenu();
            return;
        }

        _openParent = entry;
        _children = children;
        _childHover.Reset();
        _childScrollY = 0;

        var top = Bounds.top + _padding * Scale - _scrollY;
        for (var i = 0; i < index; i++)
        {
            top += (_entries[i].Kind == MenuEntryKind.Separator ? _separatorHeight : _rowHeight) * Scale;
        }

        var width = Math.Clamp(WidthOf(children), _minWidth * Scale, _maxWidth * Scale);

        // a list of every installed font is taller than any window, so it is bounded by the frame and lifted back inside it rather than running off the bottom where it could be neither seen nor clicked.
        var height = MathF.Min(HeightOf(children), _frame.bottom - _frame.top);
        top = MathF.Min(top, _frame.bottom - height);
        top = MathF.Max(top, _frame.top);
        ChildBounds = new D2D_RECT_F { left = Bounds.left - width, top = top, right = Bounds.left, bottom = top + height };
    }

    private float ScrollOf(IReadOnlyList<MenuEntry> entries) => ReferenceEquals(entries, _children) ? _childScrollY : _scrollY;

    public override bool OnMouseDown(float x, float y, bool doubleClick)
    {
        if (_children.Count > 0)
        {
            var childIndex = IndexAt(_children, ChildBounds, x, y, out var childRow);
            if (childIndex >= 0)
            {
                Activate(_children[childIndex], x, childRow);
                return true;
            }
        }

        var index = IndexAt(_entries, Bounds, x, y, out var row);
        if (index >= 0)
        {
            Activate(_entries[index], x, row);
            return true;
        }

        // a row that cannot be used swallows the click rather than closing the menu under it, which is what clicking somewhere genuinely else does.
        if (Contains(x, y))
            return true;

        // anywhere else dismisses it, which is what a menu does.
        Close();
        return true;
    }

    private void Activate(MenuEntry entry, float x, in D2D_RECT_F row)
    {
        switch (entry.Kind)
        {
            case MenuEntryKind.Slider:
                _dragging = entry;
                DragTo(entry, x, row);
                break;

            case MenuEntryKind.Choice:
            case MenuEntryKind.Submenu:
                return;

            default:
                entry.Invoked?.Invoke();
                break;
        }

        Changed?.Invoke();
        if (entry.ClosesMenu)
        {
            Close();
            return;
        }

        RefreshSubmenu();
    }

    // for a command whose work finishes later than the click did.
    public void Refresh() => RefreshSubmenu();

    private void RefreshSubmenu()
    {
        if (_openParent == null)
            return;

        _children = _openParent.Children?.Invoke() ?? [];
        if (_children.Count == 0)
        {
            CloseSubmenu();
            return;
        }

        var height = MathF.Min(HeightOf(_children), _frame.bottom - _frame.top);
        var top = MathF.Max(_frame.top, MathF.Min(ChildBounds.top, _frame.bottom - height));
        ChildBounds = new D2D_RECT_F { left = ChildBounds.left, top = top, right = ChildBounds.right, bottom = top + height };
        _childScrollY = Math.Clamp(_childScrollY, 0, MaxScrollOf(_children, ChildBounds));
        _childHover.Reset();
    }

    public override bool OnMouseUp()
    {
        if (_dragging == null)
            return false;

        _dragging = null;
        return true;
    }

    public override bool OnKeyDown(VIRTUAL_KEY key)
    {
        if (key != VIRTUAL_KEY.VK_ESCAPE)
            return false;

        Close();
        return true;
    }

    private void DragTo(MenuEntry entry, float x, in D2D_RECT_F row)
    {
        if (entry.SetNumber == null)
            return;

        var right = (_children.Count > 0 && _children.Contains(entry) ? ChildBounds.right : Bounds.right) - _padding * Scale;
        var left = right - _sliderWidth * Scale;
        var travel = right - left;
        if (travel <= 0)
            return;

        var ratio = Math.Clamp((x - left) / travel, 0, 1);
        var value = entry.Minimum + (entry.Maximum - entry.Minimum) * ratio;
        if (entry.Step > 0)
        {
            value = MathF.Round((float)(value / entry.Step)) * entry.Step;
        }

        entry.SetNumber(Math.Clamp(value, entry.Minimum, entry.Maximum));
    }

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        if (!IsOpen)
            return;

        Scale = resources.ChromeScale;
        _averageCharacter = resources.CaptionCharacterWidth;

        if (_hover.Advance(resources.ElapsedSeconds) | _childHover.Advance(resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        RenderList(deviceContext, resources, _entries, Bounds, ref _hover);
        if (_children.Count > 0)
        {
            RenderList(deviceContext, resources, _children, ChildBounds, ref _childHover);
        }
    }

    private void RenderList(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        IReadOnlyList<MenuEntry> entries,
        in D2D_RECT_F bounds,
        ref RowHover hover)
    {
        var radius = 6 * Scale;
        var shadow = new D2D_RECT_F
        {
            left = bounds.left + _shadow * Scale,
            top = bounds.top + _shadow * Scale,
            right = bounds.right + _shadow * Scale,
            bottom = bounds.bottom + _shadow * Scale,
        };

        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = shadow, radiusX = radius, radiusY = radius }, resources.OverlayBackgroundBrush.Object);
        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bounds, radiusX = radius, radiusY = radius }, resources.HeaderBackgroundBrush.Object);
        deviceContext.Object.DrawRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bounds, radiusX = radius, radiusY = radius }, resources.LineBrush.Object, 1, null);

        var padding = _padding * Scale;

        // rows are clipped to the menu, so a scrolled list does not draw over the window around it.
        deviceContext.PushAxisAlignedClip(bounds, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        var top = bounds.top + padding - ScrollOf(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var height = (entry.Kind == MenuEntryKind.Separator ? _separatorHeight : _rowHeight) * Scale;
            var row = new D2D_RECT_F { left = bounds.left, top = top, right = bounds.right, bottom = top + height };
            top += height;

            if (entry.Kind == MenuEntryKind.Separator)
            {
                var middle = MathF.Round((row.top + row.bottom) / 2) - 0.5f;
                deviceContext.DrawLine(
                    new D2D_POINT_2F { x = row.left + padding, y = middle },
                    new D2D_POINT_2F { x = row.right - padding, y = middle },
                    resources.LineBrush);
                continue;
            }

            resources.FillHover(deviceContext, row, hover.OpacityOf(i));

            var labelRect = new D2D_RECT_F { left = row.left + padding, top = row.top, right = row.right - padding, bottom = row.bottom };

            // the label stops short of whatever the row carries on its right, a slider, a swatch, a value.
            var textRect = labelRect;
            textRect.right -= ReserveOf(entry);

            var labelFormat = entry.PreviewFamily == null ? resources.CaptionFormat : resources.PreviewFormat(entry.PreviewFamily);
            TextDrawing.Draw(deviceContext, entry.Label, labelFormat, textRect, entry.IsInteractive ? resources.TextBrush : resources.DimTextBrush);

            switch (entry.Kind)
            {
                case MenuEntryKind.Toggle:
                    if (entry.Checked?.Invoke() == true)
                    {
                        Span<char> check = [Glyphs.Check];
                        TextDrawing.Draw(deviceContext, check, resources.GlyphFormat, EndOf(row), resources.GoodBrush);
                    }
                    break;

                case MenuEntryKind.Choice:
                case MenuEntryKind.Submenu:
                    var value = entry.Value?.Invoke();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var valueRect = labelRect;
                        valueRect.right -= _gap * Scale;
                        TextDrawing.Draw(deviceContext, value, resources.CaptionRightFormat, valueRect, resources.DimTextBrush);
                    }

                    Span<char> arrow = [Glyphs.Submenu];
                    TextDrawing.Draw(deviceContext, arrow, resources.GlyphFormat, EndOf(row), resources.DimTextBrush);
                    break;

                case MenuEntryKind.Slider:
                    RenderSlider(deviceContext, resources, entry, row);
                    break;

                case MenuEntryKind.Color:
                    RenderSwatch(deviceContext, resources, entry, row);
                    break;

                default:
                    var text = entry.Value?.Invoke();
                    if (!string.IsNullOrEmpty(text))
                    {
                        TextDrawing.Draw(deviceContext, text, resources.CaptionRightFormat, labelRect, resources.DimTextBrush);
                    }
                    break;
            }
        }

        deviceContext.PopAxisAlignedClip();
    }

    // a narrow rect at the right of a row, where a glyph goes.
    private D2D_RECT_F EndOf(in D2D_RECT_F row) => new()
    {
        left = row.right - (_padding + _gap) * Scale,
        top = row.top,
        right = row.right - _padding * Scale,
        bottom = row.bottom,
    };

    private void RenderSlider(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, MenuEntry entry, in D2D_RECT_F row)
    {
        var padding = _padding * Scale;
        var right = row.right - padding;
        var left = right - _sliderWidth * Scale;
        var centre = MathF.Round((row.top + row.bottom) / 2);
        var height = _sliderHeight * Scale;
        var track = new D2D_RECT_F { left = left, top = centre - height / 2, right = right, bottom = centre + height / 2 };
        deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = track, radiusX = height / 2, radiusY = height / 2 }, resources.BarTrackBrush.Object);

        var span = entry.Maximum - entry.Minimum;
        var ratio = span <= 0 ? 0 : Math.Clamp(((entry.Number?.Invoke() ?? 0) - entry.Minimum) / span, 0, 1);
        var thumbX = left + (float)((right - left) * ratio);
        var thumbRadius = _thumbRadius * Scale;
        deviceContext.Object.FillEllipse(
            new D2D1_ELLIPSE { point = new D2D_POINT_2F { x = thumbX, y = centre }, radiusX = thumbRadius, radiusY = thumbRadius },
            resources.SplitterHotBrush.Object);

        // the number itself, left of the track, so a slider is not the only way to read it.
        var valueRect = new D2D_RECT_F { left = row.left + padding, top = row.top, right = left - _gap * Scale, bottom = row.bottom };
        var text = entry.Value?.Invoke();
        if (!string.IsNullOrEmpty(text))
        {
            TextDrawing.Draw(deviceContext, text, resources.CaptionRightFormat, valueRect, resources.DimTextBrush);
        }
    }

    private void RenderSwatch(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, MenuEntry entry, in D2D_RECT_F row)
    {
        var padding = _padding * Scale;
        var right = row.right - padding;
        var left = right - _swatchWidth * Scale;
        var centre = MathF.Round((row.top + row.bottom) / 2);
        var height = _swatchHeight * Scale;
        var swatch = new D2D_RECT_F { left = left, top = centre - height / 2, right = right, bottom = centre + height / 2 };

        var text = entry.Value?.Invoke();
        if (!string.IsNullOrEmpty(text) && D3DCOLORVALUE.TryParseFromName(text, out var color))
        {
            using var brush = deviceContext.CreateSolidColorBrush(color);
            deviceContext.FillRectangle(swatch, brush);
        }

        deviceContext.DrawRectangle(swatch, resources.LineBrush);
    }
}
