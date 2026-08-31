namespace Filociraptor.Rendering;

// one complete look. the two are written out in full rather than one derived from the other,
// because a light listing is not a dark one with its values turned around.
internal sealed class Palette
{
    public required D3DCOLORVALUE Background { get; init; }

    // the surfaces keep some of themselves over a material, since rows drawn straight onto one are hard to read.
    // a light material is mostly what is behind the window, so there the chrome needs a surface of its own too.
    public required Wash Mica { get; init; }

    public required Wash Acrylic { get; init; }
    public required D3DCOLORVALUE HeaderBackground { get; init; }
    public required D3DCOLORVALUE HeaderText { get; init; }
    public required D3DCOLORVALUE Text { get; init; }
    public required D3DCOLORVALUE DimText { get; init; }
    public required D3DCOLORVALUE DisabledText { get; init; }
    public required D3DCOLORVALUE FolderText { get; init; }
    public required D3DCOLORVALUE HiddenText { get; init; }
    public required D3DCOLORVALUE HiddenFolderText { get; init; }
    public required D3DCOLORVALUE SuperHiddenText { get; init; }
    public required D3DCOLORVALUE SuperHiddenFolderText { get; init; }
    public required D3DCOLORVALUE Selection { get; init; }
    public required D3DCOLORVALUE Hover { get; init; }
    public required D3DCOLORVALUE Line { get; init; }
    public required D3DCOLORVALUE Scrollbar { get; init; }
    public required D3DCOLORVALUE PaneBackground { get; init; }
    public required D3DCOLORVALUE Splitter { get; init; }
    public required D3DCOLORVALUE SplitterHot { get; init; }
    public required D3DCOLORVALUE BarTrack { get; init; }
    public required D3DCOLORVALUE BarFill { get; init; }
    public required D3DCOLORVALUE BarFillLow { get; init; }
    public required D3DCOLORVALUE OverlayBackground { get; init; }
    public required D3DCOLORVALUE OverlayBackgroundOnMaterial { get; init; }
    public required D3DCOLORVALUE OverlayText { get; init; }
    public required D3DCOLORVALUE Good { get; init; }
    public required D3DCOLORVALUE Bad { get; init; }

    public static Palette Dark { get; } = new()
    {
        Background = new(0xFF1B1B1BU),
        Mica = new() { List = new(0x591B1B1BU), Pane = new(0x00000000U), Header = new(0x00000000U) },
        Acrylic = new() { List = new(0x591B1B1BU), Pane = new(0x00000000U), Header = new(0x00000000U) },
        HeaderBackground = new(0xFF252526U),
        HeaderText = new(0xFFB4B4B4U),
        Text = new(0xFFE4E4E4U),
        DimText = new(0xFF8C8C8CU),
        DisabledText = new(0xFF323232U),
        FolderText = new(0xFFE3C07BU),
        HiddenText = new(0x8CE4E4E4U),
        HiddenFolderText = new(0x8CE3C07BU),
        SuperHiddenText = new(0x59E4E4E4U),
        SuperHiddenFolderText = new(0x59E3C07BU),
        Selection = new(0xFF0A5A96U),
        Hover = new(0x22FFFFFFU),
        Line = new(0xFF323232U),
        Scrollbar = new(0xFF4D4D4DU),
        PaneBackground = new(0xFF202020U),
        Splitter = new(0xFF2C2C2CU),
        SplitterHot = new(0xFF0A5A96U),
        BarTrack = new(0xFF3A3A3AU),
        BarFill = new(0xFF3E8FD0U),
        BarFillLow = new(0xFFD05A5AU),
        OverlayBackground = new(0xE0101010U),
        OverlayBackgroundOnMaterial = new(0xFA101010U),
        OverlayText = new(0xFFD8D8D8U),
        Good = new(0xFF6FCF6FU),
        Bad = new(0xFFE06C6CU),
    };

    public static Palette Light { get; } = new()
    {
        Background = new(0xFFFFFFFFU),
        Mica = new() { List = new(0xD9FFFFFFU), Pane = new(0xE6FFFFFFU), Header = new(0xE6FFFFFFU) },
        Acrylic = new() { List = new(0x8CFFFFFFU), Pane = new(0xE6FFFFFFU), Header = new(0xE6FFFFFFU) },
        HeaderBackground = new(0xFFF3F3F3U),
        HeaderText = new(0xFF5C5C5CU),
        Text = new(0xFF1A1A1AU),
        DimText = new(0xFF6B6B6BU),
        DisabledText = new(0xFFA0A0A0U),
        FolderText = new(0xFF8A6516U),
        HiddenText = new(0x8C1A1A1AU),
        HiddenFolderText = new(0x8C8A6516U),
        SuperHiddenText = new(0x591A1A1AU),
        SuperHiddenFolderText = new(0x598A6516U),
        Selection = new(0xFFCCE8FFU),
        Hover = new(0x1A000000U),
        Line = new(0xFFE2E2E2U),
        Scrollbar = new(0xFFAFAFAFU),
        PaneBackground = new(0xFFF3F3F3U),
        Splitter = new(0xFFE2E2E2U),
        SplitterHot = new(0xFF0A5A96U),
        BarTrack = new(0xFFDCDCDCU),
        BarFill = new(0xFF2A7CBFU),
        BarFillLow = new(0xFFC24040U),
        OverlayBackground = new(0xE0F9F9F9U),
        OverlayBackgroundOnMaterial = new(0xFAF9F9F9U),
        OverlayText = new(0xFF1F1F1FU),
        Good = new(0xFF1E8E3EU),
        Bad = new(0xFFC5221FU),
    };
}
