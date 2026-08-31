using ShellN.Extensions;

namespace Filociraptor.Shell;

// nothing is copied or moved here, it is handed to the shell's own drop target for the folder underneath,
// so the feedback, the menu on a right drag and the operation itself are the ones Explorer would have given.
// that folder is asked for on every move, because dragging across the listing changes what a drop lands on.
[GeneratedComClass]
internal sealed partial class ShellDropTarget(Func<POINTL, ShellItem?> targetAt) : DirectN.IDropTarget, IDisposable
{
    private IComObject<DirectN.IDropTarget>? _current;
    private ShellItem? _currentItem;
    private DirectN.IDataObject? _data;

    public HRESULT DragEnter(DirectN.IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, ref DROPEFFECT pdwEffect)
    {
        _data = pDataObj;
        return Retarget(pt, grfKeyState, ref pdwEffect);
    }

    public HRESULT DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, ref DROPEFFECT pdwEffect)
    {
        var moved = Retarget(pt, grfKeyState, ref pdwEffect);
        if (moved.IsError || _current == null)
            return moved;

        return _current.Object.DragOver(grfKeyState, pt, ref pdwEffect);
    }

    public HRESULT DragLeave()
    {
        Release();
        _data = null;
        return DirectN.Constants.S_OK;
    }

    public HRESULT Drop(DirectN.IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, ref DROPEFFECT pdwEffect)
    {
        _data = pDataObj;
        var moved = Retarget(pt, grfKeyState, ref pdwEffect);
        if (moved.IsError || _current == null)
        {
            pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            Release();
            return DirectN.Constants.S_OK;
        }

        var target = _current;
        _current = null;
        try
        {
            return target.Object.Drop(pDataObj, grfKeyState, pt, ref pdwEffect);
        }
        finally
        {
            target.Dispose();
            Release();
            _data = null;
        }
    }

    // the shell target for whatever is under the pointer now.
    // while it is the same one there is nothing to do, and when it is not, one is told it left and the other that it arrived.
    private HRESULT Retarget(POINTL pt, MODIFIERKEYS_FLAGS keys, ref DROPEFFECT effect)
    {
        var item = targetAt(pt);
        if (item != null && _currentItem != null && item.Equals(_currentItem))
        {
            item.Dispose();
            return DirectN.Constants.S_OK;
        }

        Release();
        if (item == null || _data == null)
        {
            effect = DROPEFFECT.DROPEFFECT_NONE;
            return DirectN.Constants.S_OK;
        }

        _currentItem = item;
        _current = item.BindToHandler<DirectN.IDropTarget>(ShellN.Constants.BHID_SFUIObject);
        if (_current == null)
        {
            effect = DROPEFFECT.DROPEFFECT_NONE;
            return DirectN.Constants.S_OK;
        }

        return _current.Object.DragEnter(_data, keys, pt, ref effect);
    }

    private void Release()
    {
        if (_current != null)
        {
            _current.Object.DragLeave();
            _current.Dispose();
            _current = null;
        }

        _currentItem?.Dispose();
        _currentItem = null;
    }

    public void Dispose()
    {
        Release();
        _data = null;
    }
}
