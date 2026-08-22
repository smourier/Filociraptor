namespace Filociraptor.Configuration;

internal sealed class RecentFolder
{
    public string ParsingName { get; set; } = string.Empty;

    // what to show for it, kept so the list can be drawn without resolving every entry through the shell.
    // "This PC" rather than "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}".
    public string DisplayName { get; set; } = string.Empty;

    public DateTime LastVisited { get; set; } = DateTime.Now;

    public override string ToString() => DisplayName.Length > 0 ? DisplayName : ParsingName;
}
