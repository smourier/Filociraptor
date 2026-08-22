namespace Filociraptor.Shell;

internal enum ShellImageKind
{
    // one icon shared by every file with the same extension, obtained without touching the file.
    ExtensionIcon,

    // the file carries its own icon, so it has to be asked for by path.
    FileIcon,

    Thumbnail,

    // decoded by WIC from the file itself, not a thumbnail the shell kept.
    // this is the hover preview, where the point is to see the picture properly rather than quickly.
    Image,

    // decoded by WIC as well, but from the stream the shell hands over rather than from a path, because there is no file to open.
    // this is how a picture inside an archive gets a thumbnail.
    StreamImage,
}
