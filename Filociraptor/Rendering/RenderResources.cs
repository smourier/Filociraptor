namespace Filociraptor.Rendering;

// device dependent objects, created once per device and reused for every frame.
// creating a brush or a text format inside the render loop is a COM call per item, which is exactly what a
// listing of this size cannot afford.
internal sealed class RenderResources : IDisposable
{
    private const string _uiFontFamily = "Segoe UI";
    private const string _monoFontFamily = "Consolas";

    public RenderResources(IComObject<ID2D1DeviceContext> deviceContext, float dpiScale)
    {
        DpiScale = dpiScale;
        RowHeight = MathF.Round(22 * dpiScale);
        HeaderHeight = MathF.Round(26 * dpiScale);

        using var factory = DWriteFunctions.DWriteCreateFactory();
        RowFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12.5f * dpiScale));
        RowFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RowFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        RightFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12.5f * dpiScale));
        RightFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        RightFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        RightFormat.Object.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING);

        HeaderFormat = factory.CreateTextFormat(_uiFontFamily, MathF.Round(12 * dpiScale), weight: DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD);
        HeaderFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        HeaderFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        OverlayFormat = factory.CreateTextFormat(_monoFontFamily, MathF.Round(11.5f * dpiScale));
        OverlayFormat.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        OverlayFormat.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        HeaderBackgroundBrush = deviceContext.CreateSolidColorBrush(Theme.HeaderBackground);
        HeaderTextBrush = deviceContext.CreateSolidColorBrush(Theme.HeaderText);
        TextBrush = deviceContext.CreateSolidColorBrush(Theme.Text);
        DimTextBrush = deviceContext.CreateSolidColorBrush(Theme.DimText);
        FolderTextBrush = deviceContext.CreateSolidColorBrush(Theme.FolderText);
        SelectionBrush = deviceContext.CreateSolidColorBrush(Theme.Selection);
        HoverBrush = deviceContext.CreateSolidColorBrush(Theme.Hover);
        LineBrush = deviceContext.CreateSolidColorBrush(Theme.Line);
        ScrollbarBrush = deviceContext.CreateSolidColorBrush(Theme.Scrollbar);
        OverlayBackgroundBrush = deviceContext.CreateSolidColorBrush(Theme.OverlayBackground);
        OverlayTextBrush = deviceContext.CreateSolidColorBrush(Theme.OverlayText);
        GoodBrush = deviceContext.CreateSolidColorBrush(Theme.Good);
        BadBrush = deviceContext.CreateSolidColorBrush(Theme.Bad);
    }

    public float DpiScale { get; }
    public float RowHeight { get; }
    public float HeaderHeight { get; }

    public IComObject<IDWriteTextFormat> RowFormat { get; }
    public IComObject<IDWriteTextFormat> RightFormat { get; }
    public IComObject<IDWriteTextFormat> HeaderFormat { get; }
    public IComObject<IDWriteTextFormat> OverlayFormat { get; }

    public IComObject<ID2D1Brush> HeaderBackgroundBrush { get; }
    public IComObject<ID2D1Brush> HeaderTextBrush { get; }
    public IComObject<ID2D1Brush> TextBrush { get; }
    public IComObject<ID2D1Brush> DimTextBrush { get; }
    public IComObject<ID2D1Brush> FolderTextBrush { get; }
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
        HeaderFormat.Dispose();
        OverlayFormat.Dispose();
        HeaderBackgroundBrush.Dispose();
        HeaderTextBrush.Dispose();
        TextBrush.Dispose();
        DimTextBrush.Dispose();
        FolderTextBrush.Dispose();
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
