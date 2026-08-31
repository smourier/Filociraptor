namespace Filociraptor.Shell;

// the archives Windows itself opens as folders.
// Windows 11 browses all of these and Windows 10 only the zip, which is why the option is offered on one only.
internal static class ArchiveExtensions
{
    // the first build that shipped the wider archive support, everything before it browses zip and nothing else.
    private const int _firstWindows11Build = 22000;

    // see https://stackoverflow.com/a/77656328/403671
    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip",
        ".7z",
        ".gz",
        ".bz2",
        ".tar",
        ".rar",
        ".tgz",
        ".tbz2",
        ".tzst",
        ".txz",
        ".zst",
        ".xz",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup = _extensions.GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool ShownAsFolders { get; } = Environment.OSVersion.Version.Build >= _firstWindows11Build;

    // the extension comes straight from the name arena, so this costs no allocation.
    public static bool IsArchive(ReadOnlySpan<char> extension) => extension.Length > 0 && _lookup.Contains(extension);
}
