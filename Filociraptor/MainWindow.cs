namespace Filociraptor;

internal sealed class MainWindow : D3D11SwapChainWindow
{
    private const string _title = "Filociraptor";
    private const float _defaultDpi = 96;
    private const int _wheelDeltaShift = 16;
    private const int _pageJumpFallback = 20;

    private readonly FolderItems _items = new();
    private readonly DetailsView _view = new();
    private readonly PerfOverlay _overlay = new();
    private readonly PerfCounters _counters = new();

    private IComObject<ID2D1Device>? _d2dDevice;
    private IComObject<ID2D1DeviceContext>? _deviceContext;
    private IComObject<ID2D1Bitmap1>? _target;
    private RenderResources? _resources;
    private CancellationTokenSource? _scan;
    private string _path = string.Empty;
    private bool _continuous;

    public MainWindow()
        : base(_title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW)
    {
        InvalidateOnTick = false;
        _view.ItemActivated = OnItemActivated;
        _view.SortRequested = OnSortRequested;
    }

    // the base constructor creates the window, and the device with it, so this has to be a property override.
    // assigning the flag in the constructor body would happen long after the device was built without it.
    // Direct2D refuses to sit on a D3D11 device created without BGRA support, and says only E_INVALIDARG about it.
    protected override D3D11_CREATE_DEVICE_FLAG DeviceCreateFlags
    {
        get => base.DeviceCreateFlags | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        set => base.DeviceCreateFlags = value;
    }

    private float DpiScale => Dpi.width / _defaultDpi;

    // the window class needs CS_DBLCLKS for WM_LBUTTONDBLCLK to ever arrive.
    protected override void RegisterClass(string className, nint windowProc, Icon? icon = null) =>
        RegisterWindowClass(
            className,
            windowProc,
            WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW | WNDCLASS_STYLES.CS_DBLCLKS,
            icon,
            background: new HBRUSH());

    private void EnsureDeviceResources(IComObject<ID3D11Device> device)
    {
        if (_deviceContext != null)
            return;

        using var dxgiDevice = device.As<IDXGIDevice>()!;
        _d2dDevice = D2D1Functions.D2D1CreateDevice(dxgiDevice);
        _deviceContext = _d2dDevice.CreateDeviceContext();

        // work in pixels and scale by hand, so hit testing and layout share one coordinate space.
        _deviceContext.Object.SetDpi(_defaultDpi, _defaultDpi);

        // the target is opaque, which is what keeps subpixel antialiasing available for a listing that is all text.
        _deviceContext.Object.SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE);

        _resources = new RenderResources(_deviceContext, DpiScale);
    }

    protected override void DisposeDeviceDependentResources()
    {
        _resources?.Dispose();
        _resources = null;
        _deviceContext?.Dispose();
        _deviceContext = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        base.DisposeDeviceDependentResources();
    }

    protected override void CreateSwapChainDependentResources(IComObject<ID3D11Device> device, IComObject<IDXGISwapChain1> swapChain)
    {
        base.CreateSwapChainDependentResources(device, swapChain);

        // the base class calls this before the device dependent hook, so the Direct2D device has to be built here.
        EnsureDeviceResources(device);
        var deviceContext = _deviceContext!;

        using var surface = swapChain.GetBuffer<IDXGISurface>(0);
        var properties = new D2D1_BITMAP_PROPERTIES1
        {
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE,
            },
            dpiX = _defaultDpi,
            dpiY = _defaultDpi,
        };

        _target = deviceContext.CreateBitmapFromDxgiSurface(surface, properties);
        deviceContext.Object.SetTarget(_target.Object);
    }

    protected override void DisposeSwapChainDependentResources()
    {
        _deviceContext?.Object.SetTarget(null);
        _target?.Dispose();
        _target = null;
        base.DisposeSwapChainDependentResources();
    }

    protected override void Render(IComObject<ID3D11DeviceContext> deviceContext, IComObject<IDXGISwapChain1> swapChain)
    {
        var context = _deviceContext;
        var resources = _resources;
        if (context == null || resources == null)
            return;

        // a monitor change means new metrics, the text formats are sized in pixels.
        if (Math.Abs(resources.DpiScale - DpiScale) > float.Epsilon)
        {
            resources.Dispose();
            resources = new RenderResources(context, DpiScale);
            _resources = resources;
        }

        _counters.BeginFrame();

        var client = ClientRect;
        var bounds = new D2D_RECT_F { left = 0, top = 0, right = client.Width, bottom = client.Height };

        _view.Items = _items;
        _view.Bounds = bounds;
        _counters.ItemCount = _items.Count;
        _counters.BufferBytes = _items.BufferBytes;

        context.BeginDraw();
        context.Clear(Theme.Background);
        _view.Render(context, resources);
        _overlay.Render(context, resources, _counters, bounds);
        context.EndDraw();

        _counters.EndFrame();

        // no wait on the present queue, the frame is already the newest thing we have to show.
        swapChain.Present(0, 0);

        if (_continuous)
        {
            Invalidate();
        }
    }

    private void RenderNow()
    {
        RenderCore();
        Validate();
    }

    public bool HasDevice => _deviceContext != null;
    public int ItemCount => _items.Count;
    public double LastFrameMilliseconds => _counters.LastFrameMilliseconds;

    // used by the self test, which has no message loop to drive an asynchronous navigation.
    public void LoadSynchronously(string path)
    {
        _path = path;
        _items.Reset();
        _view.Reset();
        DirectoryScanner.Scan(path, _items, null, CancellationToken.None);
        SortItems(SortColumn.Name, false);
    }

    public bool RenderOnce() => _deviceContext != null && _resources != null && RenderCore();

    public void Navigate(string path) => _ = NavigateAsync(path);

    private async Task NavigateAsync(string path)
    {
        var previous = _scan;
        if (previous != null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        var scan = new CancellationTokenSource();
        _scan = scan;

        _path = path;
        Text = _title + " - " + path;
        _items.Reset();
        _view.Reset();
        _counters.ScanMilliseconds = 0;
        _counters.SortMilliseconds = 0;
        _counters.FirstRowsMilliseconds = 0;

        var start = Stopwatch.GetTimestamp();
        var allocated = GC.GetTotalAllocatedBytes(false);
        var firstBatch = true;

        try
        {
            await foreach (var count in DirectoryScanner.ScanAsync(path, _items, scan.Token).ConfigureAwait(true))
            {
                if (firstBatch)
                {
                    firstBatch = false;
                    _counters.FirstRowsMilliseconds = MillisecondsSince(start);
                }

                _counters.ItemCount = count;
                RenderNow();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Text = _title + " - " + path + " - " + ex.Message;
            return;
        }

        _counters.ScanMilliseconds = MillisecondsSince(start);
        _counters.ScanAllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated;

        SortItems(_items.SortColumn, _items.SortDescending);
        RenderNow();
    }

    private static double MillisecondsSince(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

    private void SortItems(SortColumn column, bool descending)
    {
        var start = Stopwatch.GetTimestamp();
        _items.Sort(column, descending);
        _counters.SortMilliseconds = MillisecondsSince(start);
    }

    private void OnSortRequested(SortColumn column)
    {
        var descending = _items.SortColumn == column && !_items.SortDescending;
        SortItems(column, descending);
        RenderNow();
    }

    private void OnItemActivated(int position)
    {
        if (position < 0 || position >= _items.Count)
            return;

        ref readonly var entry = ref _items.EntryAt(position);
        if (!entry.IsDirectory)
            return;

        Navigate(Path.Join(_path, _items.NameOf(entry)));
    }

    private void NavigateUp()
    {
        var parent = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(parent))
            return;

        Navigate(parent);
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case MessageDecoder.WM_MOUSEWHEEL:
                _view.ScrollByWheel((short)((wParam.Value >> _wheelDeltaShift) & 0xFFFF));
                RenderNow();
                return new LRESULT();

            case MessageDecoder.WM_MOUSEMOVE:
                if (_view.SetHover(LowWord(lParam), HighWord(lParam)))
                {
                    RenderNow();
                }
                return new LRESULT();

            case MessageDecoder.WM_LBUTTONDOWN:
                SetFocus();
                if (_view.OnClick(LowWord(lParam), HighWord(lParam), false))
                {
                    RenderNow();
                }
                return new LRESULT();

            case MessageDecoder.WM_LBUTTONDBLCLK:
                if (_view.OnClick(LowWord(lParam), HighWord(lParam), true))
                {
                    RenderNow();
                }
                return new LRESULT();

            case MessageDecoder.WM_KEYDOWN:
                if (OnKeyDown((VIRTUAL_KEY)wParam.Value))
                    return new LRESULT();

                break;
        }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }

    private bool OnKeyDown(VIRTUAL_KEY key)
    {
        var page = Math.Max(1, _view.VisibleRowCount - 2);
        switch (key)
        {
            case VIRTUAL_KEY.VK_UP:
                _view.MoveSelection(-1);
                break;

            case VIRTUAL_KEY.VK_DOWN:
                _view.MoveSelection(1);
                break;

            case VIRTUAL_KEY.VK_PRIOR:
                _view.MoveSelection(-page);
                break;

            case VIRTUAL_KEY.VK_NEXT:
                _view.MoveSelection(page);
                break;

            case VIRTUAL_KEY.VK_HOME:
                _view.Select(0);
                break;

            case VIRTUAL_KEY.VK_END:
                _view.Select(_items.Count - 1);
                break;

            case VIRTUAL_KEY.VK_RETURN:
                OnItemActivated(_view.SelectedPosition);
                return true;

            case VIRTUAL_KEY.VK_BACK:
                NavigateUp();
                return true;

            case VIRTUAL_KEY.VK_F5:
                Navigate(_path);
                return true;

            case VIRTUAL_KEY.VK_F11:
                _continuous = !_continuous;
                break;

            case VIRTUAL_KEY.VK_F12:
                _overlay.Visible = !_overlay.Visible;
                break;

            default:
                return false;
        }

        RenderNow();
        return true;
    }

    private static float LowWord(LPARAM value) => (short)(value.Value & 0xFFFF);
    private static float HighWord(LPARAM value) => (short)((value.Value >> 16) & 0xFFFF);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scan?.Cancel();
            _scan?.Dispose();
            _scan = null;
            _items.Dispose();
        }
        base.Dispose(disposing);
    }
}
