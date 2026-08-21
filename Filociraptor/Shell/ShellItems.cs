using ShellN.Extensions;

namespace Filociraptor.Shell;

internal static unsafe class ShellItems
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

        // a namespace name is not always a "path", so the file system fallback below cannot always apply to it.
        // an extension is also free to refuse to parse its own children back, and a portable device does exactly that.
        if (ShellLocation.IsNamespaceName(path))
        {
            item = ShellItem.FromSplitParsingName(path, throwOnError: false);
            if (item == null)
            {
                Application.TraceWarning($"'{path}' could not be parsed, and walking the namespace did not reach it either.");
            }

            return item;
        }

        var attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal;
        using var context = IBindCtxExtensions.CreateBindCtx(path, attributes: attributes, throwOnError: false);
        item = ShellItem.FromParsingName(path, context?.Object, throwOnError: false);
        if (item == null)
        {
            Application.TraceWarning($"'{path}' could not be parsed, with or without file system bind data.");
        }

        return item;
    }

    // an id list (PIDL) is THE identity that always works
    public static ShellItem? Bind(ReadOnlySpan<byte> idList)
    {
        if (idList.IsEmpty)
            return null;

        fixed (byte* pidl = idList)
        {
            var item = ShellItem.FromPidl((nint)pidl, throwOnError: false);
            if (item == null)
            {
                Application.TraceWarning("an item could not be bound to its id list.");
            }

            return item;
        }
    }
}
