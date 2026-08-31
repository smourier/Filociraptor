namespace Filociraptor.Shell;

internal readonly struct ShellImageRequest
{
    public required string Key { get; init; }

    // an extension like ".dll" for a shared icon, a real path for anything asked of a specific file.
    public required string Target { get; init; }
    public required int Size { get; init; }
    public required ShellImageKind Kind { get; init; }
    public required bool IsDirectory { get; init; }
    // a request that no navigation invalidates, for images that do not belong to the listing.
    public const int NeverStale = -1;

    public required int Generation { get; init; }

    // a first pass accepts only what the shell has already cached, so scrolling never waits on an extraction.
    public bool CachedOnly { get; init; }

    // how many times this has been asked for.
    // the shell can simply not answer, and one silent failure used to leave the row blank while the folder stayed open.
    public int Attempt { get; init; }

    // one real file carrying this extension, for the types whose registered icon lives inside the file itself.
    public string? SamplePath { get; init; }
}
