namespace Filociraptor.Rendering;

// Segoe MDL2 Assets came with Windows 10 and its symbols live in the private use area
// where it is missing, the symbols come from Segoe UI Symbol instead, which has shipped since Windows 7, at the code points Unicode gives them rather than at Microsoft's own.
internal static class Glyphs
{
    private const string _modernFamily = "Segoe MDL2 Assets";
    private const string _olderFamily = "Segoe UI Symbol";

    static Glyphs()
    {
        var modern = HasFamily(_modernFamily);
        Family = modern ? _modernFamily : _olderFamily;

        Back = modern ? (char)0xE112 : '←';
        Forward = modern ? (char)0xE111 : '→';
        Up = modern ? (char)0xE110 : '↑';
        Reveal = modern ? (char)0xE838 : '❐';
        Hidden = modern ? (char)0xE7B3 : '◉';
        Settings = modern ? (char)0xE713 : '⚙';
        Check = modern ? (char)0xE73E : '✓';
        Submenu = modern ? (char)0xE76C : '❯';
    }

    public static string Family { get; }
    public static char Back { get; }
    public static char Forward { get; }
    public static char Up { get; }
    public static char Reveal { get; }
    public static char Hidden { get; }
    public static char Settings { get; }
    public static char Check { get; }
    public static char Submenu { get; }

    private static bool HasFamily(string name)
    {
        try
        {
            using var factory = DWriteFunctions.DWriteCreateFactory();
            using var collection = factory.GetSystemFontCollection();
            return collection.FindFamilyNameIndex(name) >= 0;
        }
        catch (Exception ex)
        {
            Application.TraceWarning($"the installed fonts could not be read ({ex.Message}), '{_olderFamily}' is assumed.");
            return false;
        }
    }
}
