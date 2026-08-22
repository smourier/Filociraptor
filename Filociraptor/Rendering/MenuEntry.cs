namespace Filociraptor.Rendering;

// one row of the settings menu.
// the menu knows nothing about settings, it draws labels and reports what was touched, so a row is described by what it shows and what it does rather than by which option it belongs to.
internal sealed class MenuEntry
{
    public static readonly MenuEntry Separator = new() { Label = string.Empty, Kind = MenuEntryKind.Separator };

    public required string Label { get; init; }
    public MenuEntryKind Kind { get; init; }

    // what the row shows on its right, the current value read fresh every frame so it follows the setting.
    public Func<string>? Value { get; init; }
    public Func<bool>? Checked { get; init; }

    // an option this Windows cannot do at all is shown greyed and does not answer to the mouse.
    public Func<bool>? Enabled { get; init; }
    public Action? Invoked { get; init; }

    // read when the submenu opens rather than when the menu is built, so a list that changes, the recent folders, is right every time it is shown.
    public Func<IReadOnlyList<MenuEntry>>? Children { get; init; }

    // drawn in this family rather than in the menu's own, which is how a list of fonts shows what each one is.
    public string? PreviewFamily { get; init; }

    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Step { get; init; } = 1;
    public Func<double>? Number { get; init; }
    public Action<double>? SetNumber { get; init; }

    // a command is done with once it runs, a toggle or a slider is not, you change several in a row.
    public bool ClosesMenu { get; init; }

    public bool IsInteractive => Kind != MenuEntryKind.Separator && Enabled?.Invoke() != false;
}
