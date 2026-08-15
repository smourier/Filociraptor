namespace Filociraptor.Shell;

internal readonly struct ShellImageRequest
{
    public required string Key { get; init; }

    // an extension like ".dll" for a shared icon, a real path for anything asked of a specific file.
    public required string Target { get; init; }
    public required int Size { get; init; }
    public required ShellImageKind Kind { get; init; }
    public required bool IsDirectory { get; init; }
    public required int Generation { get; init; }

    // a first pass accepts only what the shell has already cached, so scrolling never waits on an extraction.
    public bool CachedOnly { get; init; }

    // one real file carrying this extension, for the types whose registered icon lives inside the file itself.
    public string? SamplePath { get; init; }
}
