using ShellN.Extensions;

namespace Filociraptor.Shell;

internal static class DataObjects
{
    public static unsafe IComObject<IDataObject>? Create(IReadOnlyList<ItemIdList> items)
    {
        if (items.Count == 0)
            return null;

        var pointers = new nint[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            pointers[i] = items[i].Pointer;
        }

        // the folder is the desktop, which is an id list of nothing but its terminator.
        // everything is a child of that one, so the items can come from anywhere and need not share a folder.
        var terminator = 0;
        fixed (nint* apidl = pointers)
        {
            var iid = typeof(IDataObject).GUID;
            var created = Functions.SHCreateDataObject((nint)(&terminator), (uint)pointers.Length, (nint)apidl, null, iid, out var unknown);
            if (created.IsError || unknown == 0)
            {
                Application.TraceError($"no data object for {items.Count} items: {created}.");
                return null;
            }

            return DirectN.Extensions.Com.ComObject.FromPointer<IDataObject>(unknown);
        }
    }
}
