using ShellN.Extensions;

namespace Filociraptor.Shell;

internal static class DragDrop
{
    // OLE has to be started on the UI thread
    public static void Initialize() => Functions.OleInitialize(0);

    public static DROPEFFECT Drag(HWND owner, IReadOnlyList<ItemIdList> items, DROPEFFECT allowed)
    {
        using var data = DataObjects.Create(items);
        if (data == null)
            return DROPEFFECT.DROPEFFECT_NONE;

        var dragged = Functions.SHDoDragDrop(owner, data.Object, null, allowed, out var effect);
        if (dragged.IsError)
        {
            Application.TraceError($"the drag did not start: {dragged}.");
        }

        return effect;
    }
}
