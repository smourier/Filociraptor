namespace Filociraptor.Rendering;

// device dependent objects, created once per device and reused for every frame.
// creating a brush or a text format inside the render loop is a COM call per item, which is exactly what a
// listing of this size cannot afford.
internal sealed class RenderResources : IDisposable
{
    private const string _uiFontFamily = "Segoe UI";
    private const string _monoFontFamily = "Consolas";
    private const string _glyphFontFamily = "Segoe MDL2 Assets";
    private const string _widthSample = @"C:\Windows\System32\drivers\etc 0123456789";

    public RenderResources(IComObject<ID2D1DeviceContext> deviceContext, float dpiScale, float zoom)
    {
        DpiScale = dpiScale * zoom;
        Zoom = zoom;
        RowHeight = MathF.Round(22 * DpiScale);
        HeaderHeight = MathF.Round(26 * DpiScale);

        using var factory = DWriteFunctions.DWriteCreateFactory();
        RowFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12.5f * DpiScale));
        RowFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RowFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        RightFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12.5f * DpiScale));
        RightFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RightFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        RightFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING);

        CenterFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12 * DpiScale));
        CenterFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        CenterFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        CenterFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        GlyphFormat = factory.CreateTextFormat(_glyphFontFamily, MathF.Round(13 * DpiScale));
        GlyphFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        GlyphFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        GlyphFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);

        HeaderFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12 * DpiScale), weight: DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD);
        HeaderFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        HeaderFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        OverlayFormat = factory.CreateTextFormat(_monoFontFamily, MathF.Round(11.5f * DpiScale));
        OverlayFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        OverlayFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        using var sample = factory.CreateTextLayout(RowFormat, _widthSample);
        sample.Object.GetMetrics(out var metrics);
        AverageCharacterWidth = MathF.Max(1, metrics.width / _widthSample.Length);

        PaneBackgroundBrush = deviceContext.CreateSolidColorBrush(Theme.PaneBackground);
        SplitterBrush = deviceContext.CreateSolidColorBrush(Theme.Splitter);
        SplitterHotBrush = deviceContext.CreateSolidColorBrush(Theme.SplitterHot);
        BarTrackBrush = deviceContext.CreateSolidColorBrush(Theme.BarTrack);
        BarFillBrush = deviceContext.CreateSolidColorBrush(Theme.BarFill);
        BarFillLowBrush = deviceContext.CreateSolidColorBrush(Theme.BarFillLow);
        HeaderBackgroundBrush = deviceContext.CreateSolidColorBrush(Theme.HeaderBackground);
        HeaderTextBrush = deviceContext.CreateSolidColorBrush(Theme.HeaderText);
        TextBrush = deviceContext.CreateSolidColorBrush(Theme.Text);
        DimTextBrush = deviceContext.CreateSolidColorBrush(Theme.DimText);
        FolderTextBrush = deviceContext.CreateSolidColorBrush(Theme.FolderText);
        HiddenTextBrush = deviceContext.CreateSolidColorBrush(Theme.HiddenText);
        HiddenFolderBrush = deviceContext.CreateSolidColorBrush(Theme.HiddenFolderText);
        SuperHiddenTextBrush = deviceContext.CreateSolidColorBrush(Theme.SuperHiddenText);
        SuperHiddenFolderBrush = deviceContext.CreateSolidColorBrush(Theme.SuperHiddenFolderText);
        SelectionBrush = deviceContext.CreateSolidColorBrush(Theme.Selection);
        HoverBrush = deviceContext.CreateSolidColorBrush(Theme.Hover);
        LineBrush = deviceContext.CreateSolidColorBrush(Theme.Line);
        ScrollbarBrush = deviceContext.CreateSolidColorBrush(Theme.Scrollbar);
        OverlayBackgroundBrush = deviceContext.CreateSolidColorBrush(Theme.OverlayBackground);
        OverlayTextBrush = deviceContext.CreateSolidColorBrush(Theme.OverlayText);
        GoodBrush = deviceContext.CreateSolidColorBrush(Theme.Good);
        BadBrush = deviceContext.CreateSolidColorBrush(Theme.Bad);
    }

    // includes the zoom factor, so everything sized from it scales together.
    public float DpiScale { get; }
    public float Zoom { get; }
    public float AverageCharacterWidth { get; }
    public float RowHeight { get; }
    public float HeaderHeight { get; }

    public IComObject<IDWriteTextFormat> RowFormat { get; }
    public IComObject<IDWriteTextFormat> RightFormat { get; }
    public IComObject<IDWriteTextFormat> CenterFormat { get; }
    public IComObject<IDWriteTextFormat> GlyphFormat { get; }
    public IComObject<IDWriteTextFormat> HeaderFormat { get; }
    public IComObject<IDWriteTextFormat> OverlayFormat { get; }

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
        RowFormat.Dispose();
        RightFormat.Dispose();
        CenterFormat.Dispose();
        GlyphFormat.Dispose();
        HeaderFormat.Dispose();
        OverlayFormat.Dispose();
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
