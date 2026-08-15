namespace Filociraptor.Rendering;

internal static class Theme
{
    public static D3DCOLORVALUE Background { get; } = new(0xFF1B1B1BU);
    public static D3DCOLORVALUE HeaderBackground { get; } = new(0xFF252526U);
    public static D3DCOLORVALUE HeaderText { get; } = new(0xFFB4B4B4U);
    public static D3DCOLORVALUE Text { get; } = new(0xFFE4E4E4U);
    public static D3DCOLORVALUE DimText { get; } = new(0xFF8C8C8CU);
    public static D3DCOLORVALUE FolderText { get; } = new(0xFFE3C07BU);
    public static D3DCOLORVALUE Selection { get; } = new(0xFF0A5A96U);
    public static D3DCOLORVALUE Hover { get; } = new(0xFF2A2A2AU);
    public static D3DCOLORVALUE Line { get; } = new(0xFF323232U);
    public static D3DCOLORVALUE Scrollbar { get; } = new(0xFF4D4D4DU);
    public static D3DCOLORVALUE OverlayBackground { get; } = new(0xE0101010U);
    public static D3DCOLORVALUE OverlayText { get; } = new(0xFFD8D8D8U);
    public static D3DCOLORVALUE Good { get; } = new(0xFF6FCF6FU);
    public static D3DCOLORVALUE Bad { get; } = new(0xFFE06C6CU);
}
