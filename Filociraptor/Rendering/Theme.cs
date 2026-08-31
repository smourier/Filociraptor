namespace Filociraptor.Rendering;

// the look everything draws with. it is read by name all over the drawing code,
// so changing it is one assignment here rather than a change at every one of those places.
internal static class Theme
{
    public static Palette Current { get; private set; } = Palette.Dark;

    public static void Use(bool dark) => Current = dark ? Palette.Dark : Palette.Light;

    public static Wash WashFor(Backdrop backdrop) => backdrop == Backdrop.Acrylic ? Current.Acrylic : Current.Mica;

    public static D3DCOLORVALUE Background => Current.Background;
    public static D3DCOLORVALUE HeaderBackground => Current.HeaderBackground;
    public static D3DCOLORVALUE HeaderText => Current.HeaderText;
    public static D3DCOLORVALUE Text => Current.Text;
    public static D3DCOLORVALUE DimText => Current.DimText;
    public static D3DCOLORVALUE DisabledText => Current.DisabledText;
    public static D3DCOLORVALUE FolderText => Current.FolderText;
    public static D3DCOLORVALUE HiddenText => Current.HiddenText;
    public static D3DCOLORVALUE HiddenFolderText => Current.HiddenFolderText;
    public static D3DCOLORVALUE SuperHiddenText => Current.SuperHiddenText;
    public static D3DCOLORVALUE SuperHiddenFolderText => Current.SuperHiddenFolderText;
    public static D3DCOLORVALUE Selection => Current.Selection;
    public static D3DCOLORVALUE Hover => Current.Hover;
    public static D3DCOLORVALUE Line => Current.Line;
    public static D3DCOLORVALUE Scrollbar => Current.Scrollbar;
    public static D3DCOLORVALUE PaneBackground => Current.PaneBackground;
    public static D3DCOLORVALUE Splitter => Current.Splitter;
    public static D3DCOLORVALUE SplitterHot => Current.SplitterHot;
    public static D3DCOLORVALUE BarTrack => Current.BarTrack;
    public static D3DCOLORVALUE BarFill => Current.BarFill;
    public static D3DCOLORVALUE BarFillLow => Current.BarFillLow;
    public static D3DCOLORVALUE OverlayBackground => Current.OverlayBackground;
    public static D3DCOLORVALUE OverlayBackgroundOnMaterial => Current.OverlayBackgroundOnMaterial;
    public static D3DCOLORVALUE OverlayText => Current.OverlayText;
    public static D3DCOLORVALUE Good => Current.Good;
    public static D3DCOLORVALUE Bad => Current.Bad;
}
