using ShellN.Extensions;
using ShellN.Extensions.Utilities;

namespace Filociraptor.Shell;

internal static class ClipboardOperations
{
    public static void Copy(IReadOnlyList<ItemIdList> items) => Put(items, DROPEFFECT.DROPEFFECT_COPY);

    public static void Cut(IReadOnlyList<ItemIdList> items) => Put(items, DROPEFFECT.DROPEFFECT_MOVE);

    private static void Put(IReadOnlyList<ItemIdList> items, DROPEFFECT effect)
    {
        using var data = DataObjects.Create(items);
        if (data == null)
            return;

        try
        {
            data.Object.SetPreferredDropEffect(effect);
            Clipboard.SetDataObject(data);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Application.TraceError($"the {items.Count} items could not be put on the clipboard: {ex}");
        }
    }

    public static void Paste(HWND owner, ShellItem destination)
    {
        try
        {
            using var data = Clipboard.GetDataObject(false);
            if (data == null)
                return;

            var items = ShellItem.ArrayFromDataObject(data.ComObject, throwOnError: false);
            if (items.Count == 0)
                return;

            var effect = data.ComObject.Object.GetPreferredDropEffect(DROPEFFECT.DROPEFFECT_COPY);
            using var operation = new FileOperation();
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(ShellN.FILEOPERATION_FLAGS.FOF_ALLOWUNDO);

            var natives = items.Select(i => i.NativeObject).ToArray();
            if (effect.HasFlag(DROPEFFECT.DROPEFFECT_MOVE))
            {
                operation.MoveItems(natives, destination.NativeObject);
            }
            else
            {
                operation.CopyItems(natives, destination.NativeObject);
            }

            foreach (var item in items)
            {
                item.Dispose();
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"the clipboard could not be pasted: {ex}");
        }
    }
}
