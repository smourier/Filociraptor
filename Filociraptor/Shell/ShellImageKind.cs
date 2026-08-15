namespace Filociraptor.Shell;

internal enum ShellImageKind
{
    // one icon shared by every file with the same extension, obtained without touching the file.
    ExtensionIcon,

    // the file carries its own icon, so it has to be asked for by path.
    FileIcon,

    Thumbnail,

    // decoded by WIC from the file itself, not a thumbnail the shell kept. this is the hover preview, where the
    // point is to see the picture properly rather than quickly.
    Image,
}
