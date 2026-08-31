namespace Filociraptor.Rendering;

// device dependent objects, created once per device and reused for every frame.
// creating a brush or a text format inside the render loop is a COM call per item, which is exactly what a listing of this size cannot afford.
internal sealed class RenderResources : IDisposable
{
    private const string _defaultUiFontFamily = "Segoe UI";

    private readonly Dictionary<string, IComObject<IDWriteTextFormat>?> _previewFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly IComObject<IDWriteFactory> _factory;
    private readonly float _baseSize;

    // the row and header heights the layout was drawn against, as multiples of the base font size, so the default size gives exactly the 22 and 26 pixels it always did.
    private const float _rowHeightRatio = 22 / 12.5f;
    private const float _headerHeightRatio = 26 / 12.5f;
    private const float _cellSpacingRatio = 10 / 12.5f;
    private const float _labelHeightRatio = 18 / 12.5f;
    private const string _monoFontFamily = "Consolas";
    private const string _widthSample = @"C:\Windows\System32\drivers\etc 0123456789";
    private const float _glyphSize = 13;

    public RenderResources(IComObject<ID2D1DeviceContext> deviceContext, float dpiScale, float zoom, Settings settings)
    {
        DpiScale = dpiScale * zoom;
        Zoom = zoom;

        var uiFontFamily = string.IsNullOrWhiteSpace(settings.FontFamily) ? _defaultUiFontFamily : settings.FontFamily;
        var baseSize = (float)settings.FontSize;

        RowHeight = MathF.Round(baseSize * _rowHeightRatio * DpiScale);
        HeaderHeight = MathF.Round(baseSize * _headerHeightRatio * DpiScale);

        CellSpacing = MathF.Round(baseSize * _cellSpacingRatio * DpiScale);
        LabelHeight = MathF.Round(baseSize * _labelHeightRatio * DpiScale);

        _baseSize = baseSize;
        _factory = DWriteFunctions.DWriteCreateFactory();
        var factory = _factory;
        RowFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round(baseSize * DpiScale));
        RowFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RowFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        RightFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round(baseSize * DpiScale));
        RightFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RightFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        RightFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING);

        CenterFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round((baseSize - 0.5f) * DpiScale));
        CenterFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CenterFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        CenterFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        CenterWrapFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round((baseSize - 0.5f) * DpiScale));
        CenterWrapFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
        CenterWrapFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_WRAP);
        CenterWrapFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        GlyphFormat = factory.CreateTextFormat(Glyphs.Family, MathF.Round(_glyphSize * DpiScale));
        GlyphFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        GlyphFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        GlyphFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        HeaderFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round((baseSize - 0.5f) * DpiScale), weight: DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD);
        HeaderFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        HeaderFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        OverlayFormat = factory.CreateTextFormat(_monoFontFamily, MathF.Round(11.5f * DpiScale));
        OverlayFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        OverlayFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        // the caption is chrome, not content. it follows the monitor and not the zoom, the way an ordinary window title bar does, so it needs its own formats at the plain dpi scale.
        ChromeScale = dpiScale;
        CaptionFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round(baseSize * ChromeScale));
        CaptionFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CaptionFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        CaptionCenterFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round(baseSize * ChromeScale));
        CaptionCenterFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CaptionCenterFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        CaptionCenterFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        CaptionRightFormat = factory.CreateTextFormat(uiFontFamily, MathF.Round(baseSize * ChromeScale));
        CaptionRightFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CaptionRightFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        CaptionRightFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING);

        CaptionGlyphFormat = factory.CreateTextFormat(Glyphs.Family, MathF.Round(_glyphSize * ChromeScale));
        CaptionGlyphFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CaptionGlyphFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        CaptionGlyphFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        using var sample = factory.CreateTextLayout(RowFormat, _widthSample);
        sample.Object.GetMetrics(out var metrics);
        AverageCharacterWidth = MathF.Max(1, metrics.width / _widthSample.Length);

        using var captionSample = factory.CreateTextLayout(CaptionFormat, _widthSample);
        captionSample.Object.GetMetrics(out var captionMetrics);
        CaptionCharacterWidth = MathF.Max(1, captionMetrics.width / _widthSample.Length);

        var translucent = settings.Backdrop != Backdrop.None;
        var wash = Theme.WashFor(settings.Backdrop);
        PaneBackgroundBrush = deviceContext.CreateSolidColorBrush(translucent ? wash.Pane : Theme.PaneBackground);
        ListBackgroundBrush = deviceContext.CreateSolidColorBrush(translucent ? wash.List : Theme.Background);
        SplitterBrush = deviceContext.CreateSolidColorBrush(Theme.Splitter);
        SplitterHotBrush = deviceContext.CreateSolidColorBrush(Theme.SplitterHot);
        BarTrackBrush = deviceContext.CreateSolidColorBrush(Theme.BarTrack);
        BarFillBrush = deviceContext.CreateSolidColorBrush(Theme.BarFill);
        BarFillLowBrush = deviceContext.CreateSolidColorBrush(Theme.BarFillLow);
        HeaderBackgroundBrush = deviceContext.CreateSolidColorBrush(translucent ? wash.Header : Theme.HeaderBackground);
        HeaderTextBrush = deviceContext.CreateSolidColorBrush(Theme.HeaderText);
        TextBrush = deviceContext.CreateSolidColorBrush(settings.Text);
        DimTextBrush = deviceContext.CreateSolidColorBrush(Theme.DimText);
        DisabledTextBrush = deviceContext.CreateSolidColorBrush(Theme.DisabledText);
        FolderTextBrush = deviceContext.CreateSolidColorBrush(Theme.FolderText);
        HiddenTextBrush = deviceContext.CreateSolidColorBrush(Theme.HiddenText);
        HiddenFolderBrush = deviceContext.CreateSolidColorBrush(Theme.HiddenFolderText);
        SuperHiddenTextBrush = deviceContext.CreateSolidColorBrush(Theme.SuperHiddenText);
        SuperHiddenFolderBrush = deviceContext.CreateSolidColorBrush(Theme.SuperHiddenFolderText);
        SelectionBrush = deviceContext.CreateSolidColorBrush(Theme.Selection);
        HoverBrush = deviceContext.CreateSolidColorBrush(Theme.Hover);
        LineBrush = deviceContext.CreateSolidColorBrush(Theme.Line);
        ScrollbarBrush = deviceContext.CreateSolidColorBrush(Theme.Scrollbar);
        OverlayBackgroundBrush = deviceContext.CreateSolidColorBrush(translucent ? Theme.OverlayBackgroundOnMaterial : Theme.OverlayBackground);
        OverlayTextBrush = deviceContext.CreateSolidColorBrush(Theme.OverlayText);
        GoodBrush = deviceContext.CreateSolidColorBrush(Theme.Good);
        BadBrush = deviceContext.CreateSolidColorBrush(Theme.Bad);
    }

    // how long the last frame took, which is what every hover fade advances by.
    public float ElapsedSeconds { get; set; }

    // set by anything still animating, and the window draws another frame while it is set.
    public bool Animating { get; set; }

    // fills with the hover colour at whatever point of the fade the animation has reached.
    public void FillHover(IComObject<ID2D1DeviceContext> deviceContext, in D2D_RECT_F rect, float opacity, float radius = 0)
    {
        if (opacity <= 0)
            return;

        HoverBrush.Object.SetOpacity(opacity);
        if (radius > 0)
        {
            deviceContext.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = radius, radiusY = radius }, HoverBrush.Object);
        }
        else
        {
            deviceContext.FillRectangle(rect, HoverBrush);
        }

        HoverBrush.Object.SetOpacity(1);
    }

    // a text format per font family, built only for the families actually drawn, which for a menu is the dozen rows on screen rather than the several hundred installed.
    public IComObject<IDWriteTextFormat> PreviewFormat(string family)
    {
        if (_previewFormats.TryGetValue(family, out var cached))
            return cached ?? CaptionFormat;

        IComObject<IDWriteTextFormat>? format = null;
        try
        {
            format = _factory.CreateTextFormat(family, MathF.Round(_baseSize * ChromeScale));
            format.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            format.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"no preview for '{family}': {ex.Message}");
        }

        _previewFormats[family] = format;
        return format ?? CaptionFormat;
    }

    // includes the zoom factor, so everything sized from it scales together.
    public float DpiScale { get; }
    public float Zoom { get; }
    public float AverageCharacterWidth { get; }

    // the scale of the window's own caption, which the zoom does not touch.
    public float ChromeScale { get; }
    public float CaptionCharacterWidth { get; }
    public float RowHeight { get; }
    public float HeaderHeight { get; }
    public float CellSpacing { get; }
    public float LabelHeight { get; }

    public IComObject<IDWriteTextFormat> RowFormat { get; }
    public IComObject<IDWriteTextFormat> RightFormat { get; }
    public IComObject<IDWriteTextFormat> CenterFormat { get; }
    public IComObject<IDWriteTextFormat> CenterWrapFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionCenterFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionRightFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionGlyphFormat { get; }
    public IComObject<IDWriteTextFormat> GlyphFormat { get; }
    public IComObject<IDWriteTextFormat> HeaderFormat { get; }
    public IComObject<IDWriteTextFormat> OverlayFormat { get; }

    public IComObject<ID2D1Brush> ListBackgroundBrush { get; }
    public IComObject<ID2D1Brush> PaneBackgroundBrush { get; }
    public IComObject<ID2D1Brush> SplitterBrush { get; }
    public IComObject<ID2D1Brush> SplitterHotBrush { get; }
    public IComObject<ID2D1Brush> BarTrackBrush { get; }
    public IComObject<ID2D1Brush> BarFillBrush { get; }
    public IComObject<ID2D1Brush> BarFillLowBrush { get; }
    public IComObject<ID2D1Brush> HeaderBackgroundBrush { get; }
    public IComObject<ID2D1Brush> HeaderTextBrush { get; }
    public IComObject<ID2D1Brush> TextBrush { get; }
    public IComObject<ID2D1Brush> DimTextBrush { get; }

    public IComObject<ID2D1Brush> DisabledTextBrush { get; }
    public IComObject<ID2D1Brush> FolderTextBrush { get; }
    public IComObject<ID2D1Brush> HiddenTextBrush { get; }
    public IComObject<ID2D1Brush> HiddenFolderBrush { get; }
    public IComObject<ID2D1Brush> SuperHiddenTextBrush { get; }
    public IComObject<ID2D1Brush> SuperHiddenFolderBrush { get; }

    // hidden files are shown faded rather than merely listed, so they read as different at a glance.
    public IComObject<ID2D1Brush> NameBrush(in FileEntry entry, bool selected)
    {
        if (selected)
            return TextBrush;

        if (entry.IsSuperHidden)
            return entry.IsDirectory ? SuperHiddenFolderBrush : SuperHiddenTextBrush;

        if (entry.IsHidden)
            return entry.IsDirectory ? HiddenFolderBrush : HiddenTextBrush;

        return entry.IsDirectory ? FolderTextBrush : TextBrush;
    }

    public static float OpacityOf(in FileEntry entry) => entry.IsSuperHidden ? 0.35f : entry.IsHidden ? 0.55f : 1;
    public IComObject<ID2D1Brush> SelectionBrush { get; }
    public IComObject<ID2D1Brush> HoverBrush { get; }
    public IComObject<ID2D1Brush> LineBrush { get; }
    public IComObject<ID2D1Brush> ScrollbarBrush { get; }
    public IComObject<ID2D1Brush> OverlayBackgroundBrush { get; }
    public IComObject<ID2D1Brush> OverlayTextBrush { get; }
    public IComObject<ID2D1Brush> GoodBrush { get; }
    public IComObject<ID2D1Brush> BadBrush { get; }

    public void Dispose()
    {
        foreach (var format in _previewFormats.Values)
        {
            format?.Dispose();
        }

        _previewFormats.Clear();
        _factory.Dispose();
        RowFormat.Dispose();
        RightFormat.Dispose();
        CenterFormat.Dispose();
        CenterWrapFormat.Dispose();
        CaptionFormat.Dispose();
        CaptionCenterFormat.Dispose();
        CaptionRightFormat.Dispose();
        CaptionGlyphFormat.Dispose();
        GlyphFormat.Dispose();
        HeaderFormat.Dispose();
        OverlayFormat.Dispose();
        ListBackgroundBrush.Dispose();
        PaneBackgroundBrush.Dispose();
        SplitterBrush.Dispose();
        SplitterHotBrush.Dispose();
        BarTrackBrush.Dispose();
        BarFillBrush.Dispose();
        BarFillLowBrush.Dispose();
        HeaderBackgroundBrush.Dispose();
        HeaderTextBrush.Dispose();
        TextBrush.Dispose();
        DimTextBrush.Dispose();
        DisabledTextBrush.Dispose();
        FolderTextBrush.Dispose();
        HiddenTextBrush.Dispose();
        HiddenFolderBrush.Dispose();
        SuperHiddenTextBrush.Dispose();
        SuperHiddenFolderBrush.Dispose();
        SelectionBrush.Dispose();
        HoverBrush.Dispose();
        LineBrush.Dispose();
        ScrollbarBrush.Dispose();
        OverlayBackgroundBrush.Dispose();
        OverlayTextBrush.Dispose();
        GoodBrush.Dispose();
        BadBrush.Dispose();
    }
}
