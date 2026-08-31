using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// where the listing is. most of the time that is a folder on disk, and then the fast path reads it directly.
// the rest of the time it is somewhere the shell knows about and the file system does not, This PC or the recycle bin, and only the namespace can answer for it.
internal sealed class ShellLocation
{
    private const string _namespacePrefix = "::";

    public static readonly string ComputerParsingName = _namespacePrefix + ShellN.Constants.CLSID_MyComputer.ToString("B");

    public static bool IsNamespaceName(string name) => name.StartsWith(_namespacePrefix, StringComparison.Ordinal);

    public required string ParsingName { get; init; }
    public required string DisplayName { get; init; }

    // the folder on disk, null when there is not one. this is what decides which enumerator runs.
    public string? Path { get; init; }

    // the id list of the folder, kept because a parsing name does not always find it again.
    public byte[]? IdList { get; init; }

    public bool IsFileSystem => Path != null;

    // an archive, which the shell describes as a folder and a stream at once.
    // what is inside one is a file with a name and an extension, and nothing the file system can open.
    public bool HoldsStreams { get; init; }

    public static ShellLocation ForPath(string path) => new()
    {
        ParsingName = path,
        DisplayName = path,
        Path = path,
    };

    // the folder itself, by id list when there is one and by name otherwise.
    public ShellItem? Bind() => ShellItems.Bind(IdList) ?? ShellItems.Parse(ParsingName, true);

    public static ShellLocation? Resolve(string parsingName)
    {
        if (string.IsNullOrEmpty(parsingName))
            return null;

        using var item = ShellItems.Parse(parsingName, true);
        if (item == null)
            return null;

        return From(item);
    }

    public static ShellLocation? From(ShellItem item)
    {
        var display = item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false);
        var parsing = item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false);
        if (string.IsNullOrEmpty(parsing))
            return null;

        var path = item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, false);
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            path = null;
        }

        return new ShellLocation
        {
            ParsingName = parsing,
            DisplayName = string.IsNullOrEmpty(display) ? parsing : display,
            Path = path,
            HoldsStreams = path == null && ShellItems.IsStreamFolder(item),

            // only where it is needed.
            IdList = path == null ? item.GetIdListAsByteArray(false) : null,
        };
    }

    public ShellLocation? GetParent()
    {
        if (string.Equals(ParsingName, ComputerParsingName, StringComparison.OrdinalIgnoreCase))
            return null;

        using var item = Bind();
        if (item == null)
            return null;

        item.NativeObject.GetParent(out var parent);
        using var parentItem = ShellItem.FromObject(parent);
        return parentItem == null ? null : From(parentItem);
    }
}
