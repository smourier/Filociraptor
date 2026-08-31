using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// the roots the shell offers, which is what Explorer lists in its tree.
// the Desktop is the root of the namespace and This PC is one of its children,
// so its children are exactly that list, with the Desktop node itself left out because it is the thing they all hang from.
internal static class PlacesScanner
{
    public static async Task<IReadOnlyList<PlaceEntry>> ScanAsync(CancellationToken cancellationToken) => await Task.Run(Enumerate, cancellationToken).ConfigureAwait(true);

    private static List<PlaceEntry> Enumerate()
    {
        var places = new List<PlaceEntry>();
        try
        {
            // shared and cached by ShellN, and not ours to dispose.
            var desktop = ShellFolder.Desktop;

            // folders only, the way a tree does it.
            foreach (var child in desktop.EnumerateChildren(_SHCONTF.SHCONTF_FOLDERS))
            {
                var name = child.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false);
                var parsing = child.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false);
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(parsing))
                {
                    places.Add(new PlaceEntry
                    {
                        ParsingName = parsing,
                        DisplayName = name,
                        IdList = child.GetIdListAsByteArray(false),
                    });
                }

                child.Dispose();
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"the desktop's children could not be listed: {ex}");
        }

        return places;
    }
}
