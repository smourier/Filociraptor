using ShellN.Extensions;

namespace Filociraptor.Shell;

// the site a shell context menu asks for while it is up.
[GeneratedComClass]
internal sealed partial class ContextMenuSite(HWND owner) :
    DirectN.IServiceProvider,
    IObjectWithSite,
    IOleWindow,
    ShellN.IHandlerActivationHost,
    IDisposable
{
    private nint _site;

    public string? NavigateToParsingName { get; set; }

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

    public HRESULT BeforeCoCreateInstance(in Guid clsidHandler, ShellN.IShellItemArray itemsBeingActivated, ShellN.IHandlerInfo handlerInfo)
    {
        if (clsidHandler == ShellN.Constants.ExecuteFolder)
        {
            foreach (var item in itemsBeingActivated.Enumerate(true))
            {
                var parsing = item.SIGDN_DESKTOPABSOLUTEPARSING;
                item.Dispose();
                if (!string.IsNullOrEmpty(parsing))
                {
                    NavigateToParsingName = parsing;
                    // navigate on first item only, because the shell will only open one window anyway.
                    return Constants.ERROR_CANCELLED;
                }
            }

            return Constants.S_OK;
        }

#if DEBUG
        handlerInfo.GetApplicationDisplayName(out var name);
        handlerInfo.GetApplicationPublisher(out var pub);
        handlerInfo.GetApplicationIconReference(out var iconReference);
        Application.TraceVerbose($"{clsidHandler:B} name:'{name}' publisher:'{pub}' icon:'{iconReference}'");
        PWSTR.Dispose(ref name);
        PWSTR.Dispose(ref pub);
        PWSTR.Dispose(ref iconReference);
#endif
        return Constants.S_OK;
    }

    public HRESULT BeforeCreateProcess(PWSTR applicationPath, PWSTR commandLine, ShellN.IHandlerInfo handlerInfo)
    {
        //Application.TraceVerbose($"{applicationPath} {commandLine}");
        return Constants.S_OK;
    }

}
