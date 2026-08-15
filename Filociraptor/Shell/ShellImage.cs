namespace Filociraptor.Shell;

// pixels handed from a worker to the UI thread.
// the shell object itself never crosses threads, only this, which removes every apartment question from the design.
internal sealed class ShellImage
{
    public required string Key { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required byte[] Pixels { get; init; }
    public required int Generation { get; init; }
    public string? TypeName { get; init; }

    // shown immediately, but a proper extraction is on its way to replace it.
    public bool Provisional { get; set; }
}
