namespace Filociraptor.Shell;

// one of the Desktop folder's children, This PC, the recycle bin, the network, the user's own folder.
// it carries its id list for the same reason a listing does, a parsing name does not always find it again.
internal sealed class PlaceEntry
{
    public required string ParsingName { get; init; }
    public required string DisplayName { get; init; }
    public byte[]? IdList { get; init; }
}
