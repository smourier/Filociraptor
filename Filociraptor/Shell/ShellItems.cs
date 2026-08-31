using ShellN.Extensions;
using SFGAO = ShellN.SFGAO_FLAGS;

namespace Filociraptor.Shell;

internal static unsafe class ShellItems
{
    // deep enough for any real namespace, and a stop in case one ever loops.
    private const int _maxParentWalk = 16;

    // what the shell says it is rather than what the file system does, which is the whole difference for an archive, a file on disk and a folder here.
    public static bool IsFolder(ShellItem item) => item.Attributes.HasFlag(SFGAO.SFGAO_FOLDER);

    // an archive is a folder and a stream at once, and the shell says so itself rather than being asked about the extension.
    // a folder inside an archive is only a folder though, its own attributes carry nothing that says where it lives, so the question goes up to its parents until one of them answers it.
    public static bool IsStreamFolder(ShellItem item)
    {
        var current = item;
        var owned = false;
        try
        {
            for (var depth = 0; current != null && depth < _maxParentWalk; depth++)
            {
                var attributes = current.Attributes;
                if (attributes.HasFlag(SFGAO.SFGAO_FOLDER) && attributes.HasFlag(SFGAO.SFGAO_STREAM))
                    return true;

                // a real directory ends the walk, nothing with one of those behind it is inside an archive.
                if (attributes.HasFlag(SFGAO.SFGAO_FOLDER) && attributes.HasFlag(SFGAO.SFGAO_FILESYSTEM))
                    return false;

                current.NativeObject.GetParent(out var parent);
                var next = parent == null ? null : ShellItem.FromObject(parent);
                if (owned)
                {
                    current.Dispose();
                }

                current = next;
                owned = true;
            }
        }
        finally
        {
            if (owned)
            {
                current?.Dispose();
            }
        }

        return false;
    }

    // whether something is still there, asked of the shell because it is the only one that can answer for all of them.
    public static bool Exists(string parsingName)
    {
        if (string.IsNullOrEmpty(parsingName))
            return false;

        using var item = ShellItem.FromParsingName(parsingName, throwOnError: false)
            ?? (ShellLocation.IsNamespaceName(parsingName) ? ShellItem.FromSplitParsingName(parsingName, throwOnError: false) : null);
        return item != null;
    }

    // a file inside a namespace junction cannot be reached by its path alone.
    // C:\Windows\Fonts is the one everybody meets, the Fonts extension owns those names and refuses to hand back the files underneath,
    // so parsing fails and the item has no icon, no thumbnail and no context menu.
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

    // an id list (PIDL) is THE identity that always works.
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
