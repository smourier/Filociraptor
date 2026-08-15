using WicNet;

namespace Filociraptor.Shell;

// the file types WIC can actually decode, asked of WIC rather than guessed from a list of our own.
internal static class ImageExtensions
{
    private static readonly HashSet<string> _extensions = new(WicImagingComponent.DecoderFileExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup = _extensions.GetAlternateLookup<ReadOnlySpan<char>>();

    // the extension comes straight from the name arena, so this costs no allocation.
    public static bool CanDecode(ReadOnlySpan<char> extension) => extension.Length > 0 && _lookup.Contains(extension);
}
