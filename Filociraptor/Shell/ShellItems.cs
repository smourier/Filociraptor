using ShellN.Extensions;

namespace Filociraptor.Shell;

internal static class ShellItems
{
    // a file inside a namespace junction cannot be reached by its path alone.
    // C:\Windows\Fonts is the one everybody meets, the Fonts extension owns those names and refuses to hand back the files underneath,
    // so parsing fails and the item has no icon, no thumbnail and no context menu.
    // supplying the attributes the file really has makes the shell build the item from the file system instead, and everything works again.
    public static ShellItem? Parse(string path, bool isDirectory)
    {
        var item = ShellItem.FromParsingName(path, throwOnError: false);
        if (item != null)
            return item;

        var attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal;
        using var context = IBindCtxExtensions.CreateBindCtx(path, attributes: attributes, throwOnError: false);
        return ShellItem.FromParsingName(path, context?.Object, throwOnError: false);
    }
}
