using ShellN.Extensions;

namespace Filociraptor.Shell;

// dragging items out of the window.
// the data object is the shell's own, so whatever receives it gets what Explorer would have handed over.
internal static class DragDrop
{
    // OLE has to be started on the thread that drags, and the CLR's own STA is not enough for it.
    public static void Initialize() => DirectN.Functions.OleInitialize(0);

    public static void Shutdown() => DirectN.Functions.OleUninitialize();

    // the drop source is left to the shell, which is what draws the picture of what is being dragged.
    public static unsafe DROPEFFECT Drag(HWND owner, ItemIdList parent, IReadOnlyList<ItemIdList> items, DROPEFFECT allowed)
    {
        if (items.Count == 0)
            return DROPEFFECT.DROPEFFECT_NONE;

        var pointers = new nint[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            pointers[i] = items[i].Pointer;
        }

        fixed (nint* apidl = pointers)
        {
            var iid = typeof(DirectN.IDataObject).GUID;
            if (DirectN.Functions.SHCreateDataObject(parent.Pointer, (uint)pointers.Length, (nint)apidl, null, iid, out var unknown).IsError || unknown == 0)
                return DROPEFFECT.DROPEFFECT_NONE;

            using var data = DirectN.Extensions.Com.ComObject.FromPointer<DirectN.IDataObject>(unknown);
            if (data == null)
                return DROPEFFECT.DROPEFFECT_NONE;

            var effect = DROPEFFECT.DROPEFFECT_NONE;
            DirectN.Functions.SHDoDragDrop(owner, data.Object, null, allowed, out effect);
            return effect;
        }
    }
}
