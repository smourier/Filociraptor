namespace Filociraptor.Rendering;

// what the three surfaces are painted with over a material, none of them opaque.
// mica shows almost nothing and so takes a heavy hand, where acrylic is meant to be seen through and pays for it in text.
internal sealed class Wash
{
    public required D3DCOLORVALUE Pane { get; init; }

    public required D3DCOLORVALUE Header { get; init; }

    public required D3DCOLORVALUE List { get; init; }
}
