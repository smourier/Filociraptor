namespace Filociraptor.Shell;

// the site a shell context menu asks for while it is up.
// handlers use it to find the owning window, and some of them refuse to appear without one, so this is what makes the menu the real Explorer menu rather than a subset.
[GeneratedComClass]
internal sealed partial class ContextMenuSite(HWND owner) : DirectN.IServiceProvider, IObjectWithSite, IOleWindow, IDisposable
{
    private nint _site;

    public HRESULT QueryService(in Guid guidService, in Guid riid, out nint ppvObject)
    {
        ppvObject = DirectN.Extensions.Com.ComObject.GetOrCreateComInstance(this, riid, CreateComInterfaceFlags.None);
        return ppvObject == 0 ? Constants.E_NOINTERFACE : Constants.S_OK;
    }

    public HRESULT GetSite(in Guid riid, out nint ppvSite)
    {
        if (_site != 0)
            return Marshal.QueryInterface(_site, riid, out ppvSite);

        ppvSite = 0;
        return Constants.E_NOINTERFACE;
    }

    public HRESULT SetSite(nint pUnkSite)
    {
        Dispose();
        if (pUnkSite != 0)
        {
            Marshal.AddRef(pUnkSite);
        }

        _site = pUnkSite;
        return Constants.S_OK;
    }

    public HRESULT GetWindow(out HWND phwnd)
    {
        phwnd = owner;
        return Constants.S_OK;
    }

    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => Constants.E_NOTIMPL;

    public void Dispose()
    {
        var site = Interlocked.Exchange(ref _site, 0);
        if (site != 0)
        {
            Marshal.Release(site);
        }
    }
}
