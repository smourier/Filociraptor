namespace Filociraptor.FileSystem;

// one row of a listing, 32 bytes, no managed references inside.
// a folder is an array of these plus one shared character arena, so a million files cost one allocation, not a million.
internal struct FileEntry
{
    public int NameOffset;
    public int NameLength;

    // offset of the extension inside the name, dot included, negative when there is none.
    public int ExtensionOffset;
    public FileAttributes Attributes;

    // where the parsing name sits in the arena, for an item that came from the namespace. negative for a file on disk, whose parsing name is just its folder and its name.
    public int ParsingOffset;
    public int ParsingLength;
    public long Size;
    public long LastWriteTicks;

    public readonly bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    public readonly bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public readonly bool IsSuperHidden => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System);
}
