namespace Filociraptor.Rendering;

// the window draws its own caption.
// the standard one is removed by taking the whole window as client area in WM_NCCALCSIZE, and the parts Windows still needs to know about,
// the drag area and the three window buttons, are handed back through WM_NCHITTEST.
// going through the real hit test codes rather than handling clicks privately is what keeps double click to maximise, and the snap layout flyout on Windows 11, working as they should.
// the navigation buttons are the exception. they report themselves as client area, so they receive ordinary mouse messages instead of starting a window drag.
internal sealed class TitleBar : Control
{
    public const int HitClient = 1;
    public const int HitCaption = 2;
    public const int HitMinimize = 8;
    public const int HitMaximize = 9;
    public const int HitClose = 20;
    public const int HitTop = 12;

    private const float _height = 32;
    private const float _buttonWidth = 46;
    private const float _navigationWidth = 34;
    private const float _navigationCount = 5;

    // minimize, maximize and close. everything laid out from the right counts back over these.
    private const float _windowButtonCount = 3;
    private const float _titleGap = 8;
    private const float _glyphSize = 10;
    private const float _zoomWidth = 54;
    private const float _gearWidth = 34;
    private const float _sliderWidth = 118;
    private const float _radius = 4;

    // Segoe MDL2 Assets, which is present on both Windows 10 and Windows 11.
    private const char _backGlyph = (char)0xE112;
    private const char _forwardGlyph = (char)0xE111;
    private const char _upGlyph = (char)0xE110;
    private const char _revealGlyph = (char)0xE838;
    private const char _hiddenGlyph = (char)0xE7B3;
    private const char _settingsGlyph = (char)0xE713;

    private readonly ViewSlider _slider = new();

    // one per button, in the order they are drawn, so each fades on its own rather than the row flashing together.
    private readonly HoverAnimation[] _navigationHovers = new HoverAnimation[(int)_navigationCount];
    private HoverAnimation _gearHover;
    private HoverAnimation _zoomHover;
    private float _hoverX = -1;
    private float _hoverY = -1;
    private HoverAnimation _minimizeHover;
    private HoverAnimation _maximizeHover;
    private HoverAnimation _closeHover;
    private float _scale = 1;
    private string? _compactSource;
    private string? _compacted;
    private float _compactWidth;

    public int HotButton { get; set; }
    public bool IsMaximized { get; set; }
    public NavigationButton HotNavigation { get; private set; }
    public bool BackEnabled { get; set; }
    public bool ForwardEnabled { get; set; }
    public bool UpEnabled { get; set; }
    public bool RevealEnabled { get; set; } = true;
    public bool ShowHidden { get; set; }
    public Action<NavigationButton>? NavigationPressed { get; set; }
    public float Height => MathF.Round(_height * _scale);

    public ViewSlider Slider => _slider;
    public bool GearHot { get; private set; }
    public Action? SettingsPressed { get; set; }
    public Action? ZoomPressed { get; set; }

    private float NavigationRight => Bounds.left + _navigationWidth * _navigationCount * _scale;
    private float SliderLeft => ZoomBounds.left - _sliderWidth * _scale;

    // the gear sits between the zoom and the window buttons, and is ours rather than the caption's, so it has to report itself as client area or a click on it would start dragging the window instead.
    private float GearLeft => Bounds.right - _buttonWidth * _windowButtonCount * _scale - _gearWidth * _scale;
    public D2D_RECT_F GearBounds => new() { left = GearLeft, top = Bounds.top, right = GearLeft + _gearWidth * _scale, bottom = Bounds.bottom };
    private bool InGear(float x, float y) => x >= GearBounds.left && x < GearBounds.right && y >= Bounds.top && y < Bounds.bottom;

    // the zoom reads as a button too, it opens the list of sizes.
    public D2D_RECT_F ZoomBounds => new() { left = GearLeft - _zoomWidth * _scale, top = Bounds.top, right = GearLeft, bottom = Bounds.bottom };
    private bool InZoom(float x, float y) => x >= ZoomBounds.left && x < ZoomBounds.right && y >= Bounds.top && y < Bounds.bottom;

    public void Update(in D2D_RECT_F clientBounds, float scale)
    {
        _scale = scale;
        Bounds = new D2D_RECT_F { left = clientBounds.left, top = clientBounds.top, right = clientBounds.right, bottom = clientBounds.top + Height };
        _slider.Update(new D2D_RECT_F { left = SliderLeft, top = Bounds.top, right = SliderLeft + _sliderWidth * _scale, bottom = Bounds.bottom }, _scale);
    }

    public int HitTest(float x, float y)
    {
        if (y < Bounds.top || y >= Bounds.bottom || x < Bounds.left || x >= Bounds.right)
            return HitClient;

        // the navigation buttons, the slider and the gear are ours to handle, so they must not read as caption.
        if (x < NavigationRight || _slider.Contains(x, y) || InGear(x, y) || InZoom(x, y))
            return HitClient;

        var width = _buttonWidth * _scale;
        if (x >= Bounds.right - width)
            return HitClose;

        if (x >= Bounds.right - width * 2)
            return HitMaximize;

        if (x >= Bounds.right - width * 3)
            return HitMinimize;

        return HitCaption;
    }

    public NavigationButton NavigationAt(float x, float y)
    {
        if (y < Bounds.top || y >= Bounds.bottom || x < Bounds.left || x >= NavigationRight)
            return NavigationButton.None;

        var index = (int)((x - Bounds.left) / (_navigationWidth * _scale));
        return index switch
        {
            0 => NavigationButton.Back,
            1 => NavigationButton.Forward,
            2 => NavigationButton.Up,
            3 => NavigationButton.Reveal,
            4 => NavigationButton.Hidden,
            _ => NavigationButton.None,
        };
    }

    private bool SetNavigationHover(float x, float y)
    {
        var button = NavigationAt(x, y);
        if (button == HotNavigation)
            return false;

        HotNavigation = button;
        return true;
    }

    private bool SetGearHover(float x, float y)
    {
        var hot = InGear(x, y);
        if (hot == GearHot)
            return false;

        GearHot = hot;
        return true;
    }

    // the caption is the window's own, so this only ever deals with the parts that are ours, the navigation buttons, the slider and the gear.
    public override bool IsCapturing => _slider.Dragging;

    public override bool OnMouseMove(float x, float y)
    {
        if (_slider.Dragging)
        {
            _slider.Drag(x);
            return true;
        }

        var wasZoom = InZoom(_hoverX, _hoverY);
        _hoverX = x;
        _hoverY = y;

        var changed = SetNavigationHover(x, y);
        changed |= _slider.SetHover(x, y);
        changed |= SetGearHover(x, y);
        return changed || wasZoom != InZoom(x, y);
    }

    public override bool OnMouseDown(float x, float y, bool doubleClick) => _slider.BeginDrag(x, y) || OnClick(x, y);

    public override bool OnMouseUp()
    {
        if (!_slider.Dragging)
            return false;

        _slider.EndDrag();
        return true;
    }

    private bool OnClick(float x, float y)
    {
        if (InGear(x, y))
        {
            SettingsPressed?.Invoke();
            return true;
        }

        if (InZoom(x, y))
        {
            ZoomPressed?.Invoke();
            return true;
        }

        var button = NavigationAt(x, y);
        if (button == NavigationButton.None || !IsEnabled(button))
            return false;

        NavigationPressed?.Invoke(button);
        return true;
    }

    private ref HoverAnimation HoverOf(int button)
    {
        switch (button)
        {
            case HitMinimize:
                return ref _minimizeHover;

            case HitMaximize:
                return ref _maximizeHover;

            default:
                return ref _closeHover;
        }
    }

    private bool IsEnabled(NavigationButton button) => button switch
    {
        NavigationButton.Back => BackEnabled,
        NavigationButton.Forward => ForwardEnabled,
        NavigationButton.Up => UpEnabled,
        NavigationButton.Reveal => RevealEnabled,
        NavigationButton.Hidden => true,
        _ => false,
    };

    public void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, string title)
    {
        deviceContext.FillRectangle(Bounds, resources.HeaderBackgroundBrush);

        var navigationWidth = _navigationWidth * _scale;
        DrawNavigation(deviceContext, resources, NavigationButton.Back, _backGlyph, Bounds.left);
        DrawNavigation(deviceContext, resources, NavigationButton.Forward, _forwardGlyph, Bounds.left + navigationWidth);
        DrawNavigation(deviceContext, resources, NavigationButton.Up, _upGlyph, Bounds.left + navigationWidth * 2);
        DrawNavigation(deviceContext, resources, NavigationButton.Reveal, _revealGlyph, Bounds.left + navigationWidth * 3);
        DrawNavigation(deviceContext, resources, NavigationButton.Hidden, _hiddenGlyph, Bounds.left + navigationWidth * 4);

        var width = _buttonWidth * _scale;
        var zoomWidth = _zoomWidth * _scale;
        var titleRect = new D2D_RECT_F
        {
            left = NavigationRight + _titleGap * _scale,
            top = Bounds.top,
            right = SliderLeft,
            bottom = Bounds.bottom,
        };

        TextDrawing.Draw(deviceContext, Compact(title, titleRect.right - titleRect.left, resources), resources.CaptionFormat, titleRect, resources.TextBrush);

        _slider.Render(deviceContext, resources);

        // the zoom sits immediately left of the buttons, so the effect of the wheel is always visible.
        Span<char> buffer = stackalloc char[16];
        var zoom = new ScratchText(buffer);
        zoom.Append((long)MathF.Round(resources.Zoom * 100));
        zoom.Append(" %");

        var zoomRect = ZoomBounds;
        if (_zoomHover.Advance(InZoom(_hoverX, _hoverY), resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        resources.FillHover(deviceContext, zoomRect, _zoomHover.Opacity, _radius * _scale);
        TextDrawing.Draw(deviceContext, zoom.Text, resources.CaptionCenterFormat, zoomRect, resources.TextBrush);

        DrawGear(deviceContext, resources);
        DrawButton(deviceContext, resources, HitMinimize, Bounds.right - width * 3, width);
        DrawButton(deviceContext, resources, HitMaximize, Bounds.right - width * 2, width);
        DrawButton(deviceContext, resources, HitClose, Bounds.right - width, width);

        deviceContext.DrawLine(
            new D2D_POINT_2F { x = Bounds.left, y = Bounds.bottom - 0.5f },
            new D2D_POINT_2F { x = Bounds.right, y = Bounds.bottom - 0.5f },
            resources.LineBrush);
    }

    // PathCompactPathExW is what Explorer uses to fit a path into a field, replacing whole segments with an ellipsis rather than chopping the end off, so the drive and the file name both survive.
    private ReadOnlySpan<char> Compact(string title, float width, RenderResources resources)
    {
        if (width <= 0)
            return default;

        var budget = (int)(width / resources.CaptionCharacterWidth);
        if (budget >= title.Length)
            return title;

        if (_compacted != null && _compactWidth == width && _compactSource == title)
            return _compacted;

        using var buffer = new AllocPwstr((uint)((budget + 1) * 2));
        ShellN.Functions.PathCompactPathExW(buffer, PWSTR.From(title), (uint)budget, 0);

        _compactSource = title;
        _compactWidth = width;
        _compacted = buffer.ToString();
        return _compacted ?? title;
    }

    private void DrawNavigation(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, NavigationButton button, char glyph, float left)
    {
        var enabled = IsEnabled(button);
        var inset = 3 * _scale;
        var rect = new D2D_RECT_F
        {
            left = left + inset,
            top = Bounds.top + inset,
            right = left + _navigationWidth * _scale - inset,
            bottom = Bounds.bottom - inset,
        };

        var radius = _radius * _scale;
        var on = button == NavigationButton.Hidden && ShowHidden;
        if (on)
        {
            deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = radius, radiusY = radius }, resources.SelectionBrush.Object);
        }

        // a disabled button fades back out rather than dropping its highlight the instant it is disabled.
        ref var hover = ref _navigationHovers[(int)button - 1];
        if (hover.Advance(enabled && !on && HotNavigation == button, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        resources.FillHover(deviceContext, rect, hover.Opacity, radius);

        Span<char> buffer = [glyph];
        TextDrawing.Draw(deviceContext, buffer, resources.CaptionGlyphFormat, rect, enabled ? resources.TextBrush : resources.LineBrush);
    }

    private void DrawGear(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources)
    {
        var inset = 3 * _scale;
        var bounds = GearBounds;
        var rect = new D2D_RECT_F
        {
            left = bounds.left + inset,
            top = bounds.top + inset,
            right = bounds.right - inset,
            bottom = bounds.bottom - inset,
        };

        if (_gearHover.Advance(GearHot, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        resources.FillHover(deviceContext, rect, _gearHover.Opacity, _radius * _scale);

        Span<char> buffer = [_settingsGlyph];
        TextDrawing.Draw(deviceContext, buffer, resources.CaptionGlyphFormat, rect, resources.TextBrush);
    }

    private void DrawButton(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, int button, float left, float width)
    {
        var rect = new D2D_RECT_F { left = left, top = Bounds.top, right = left + width, bottom = Bounds.bottom };
        ref var hover = ref HoverOf(button);
        if (hover.Advance(HotButton == button, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        if (button == HitClose)
        {
            // close keeps its own colour, so it fades to red rather than to the ordinary grey.
            if (hover.Opacity > 0)
            {
                resources.BadBrush.Object.SetOpacity(hover.Opacity);
                deviceContext.FillRectangle(rect, resources.BadBrush);
                resources.BadBrush.Object.SetOpacity(1);
            }
        }
        else
        {
            resources.FillHover(deviceContext, rect, hover.Opacity);
        }

        var brush = resources.TextBrush;
        var size = _glyphSize * _scale;
        var centreX = (rect.left + rect.right) / 2;
        var centreY = (rect.top + rect.bottom) / 2;
        var half = size / 2;

        switch (button)
        {
            case HitMinimize:
                deviceContext.DrawLine(
                    new D2D_POINT_2F { x = centreX - half, y = centreY },
                    new D2D_POINT_2F { x = centreX + half, y = centreY },
                    brush);
                break;

            case HitMaximize:
                var square = new D2D_RECT_F { left = centreX - half, top = centreY - half, right = centreX + half, bottom = centreY + half };
                deviceContext.DrawRectangle(square, brush);
                if (IsMaximized)
                {
                    var offset = 2 * _scale;
                    var behind = new D2D_RECT_F
                    {
                        left = square.left + offset,
                        top = square.top - offset,
                        right = square.right + offset,
                        bottom = square.bottom - offset,
                    };

                    deviceContext.DrawRectangle(behind, brush);
                }
                break;

            case HitClose:
                deviceContext.DrawLine(
                    new D2D_POINT_2F { x = centreX - half, y = centreY - half },
                    new D2D_POINT_2F { x = centreX + half, y = centreY + half },
                    brush);
                deviceContext.DrawLine(
                    new D2D_POINT_2F { x = centreX + half, y = centreY - half },
                    new D2D_POINT_2F { x = centreX - half, y = centreY + half },
                    brush);
                break;
        }
    }
}
