namespace Filociraptor;

internal sealed class MainWindow : D3D11SwapChainWindow
{
    private const string _title = "Filociraptor";
    private const float _defaultDpi = 96;
    private const int _wheelDeltaShift = 16;
    private const int _hitTestClient = 1;
    private const int _sizeWECursorId = 32644;
    private const float _defaultPaneWidth = 220;
    private const float _minPaneWidth = 150;
    private const float _minListWidth = 260;
    private const float _splitterWidth = 6;
    private const int _hoverPreviewDelay = 300;
    private const float _minZoom = 0.5f;
    private const float _maxZoom = 4;
    private const float _zoomStep = 1.1f;

    private readonly FolderItems _items = new();
    private readonly DetailsView _details = new();
    private readonly GridView _grid = new();
    private readonly DrivesView _drives = new();
    private readonly TitleBar _titleBar = new();
    private readonly Stack<string> _back = [];
    private readonly Stack<string> _forward = [];
    private readonly ImagePreview _preview = new();
    private readonly Timer _hoverTimer;
    private readonly PerfOverlay _overlay = new();
    private readonly PerfCounters _counters = new();
    private readonly HCURSOR _sizeWECursor = Functions.LoadCursorW(HINSTANCE.Null, new PWSTR { Value = _sizeWECursorId });

    private D2D_RECT_F _splitterBounds;
    private float _paneWidth = _defaultPaneWidth;
    private float _splitterGrabOffset;
    private bool _splitterDragging;
    private bool _splitterHot;
    private float _lastMouseX;
    private float _lastMouseY;

    private IComObject<ID2D1Device>? _d2dDevice;
    private IComObject<ID2D1DeviceContext>? _deviceContext;
    private IComObject<ID2D1Bitmap1>? _target;
    private RenderResources? _resources;
    private readonly FolderWatcher _watcher;
    private readonly DriveNotifier _driveNotifier = new();
    private ImageCache? _images;
    private CancellationTokenSource? _scan;
    private CancellationTokenSource? _driveScan;
    private static readonly string _defaultPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private string _path = string.Empty;
    private string _titleText = _title;
    private bool _continuous;
    private ViewMode _mode = ViewMode.Details;
    private float _zoom = 1;
    private bool _showHidden;
    private float _restoreScroll;
    private int _restoreSelection = -1;

    private IItemsView View => _mode == ViewMode.Details ? _details : _grid;

    public MainWindow()
        : base(_title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW)
    {
        InvalidateOnTick = false;
        _details.ItemActivated = OnItemActivated;
        _details.SortRequested = OnSortRequested;
        _grid.ItemActivated = OnItemActivated;
        _drives.DriveActivated = drive => Navigate(drive.Root);
        _titleBar.NavigationPressed = OnNavigationButton;
        _titleBar.Slider.ModeChanged = mode => Mode = mode;
        _watcher = new FolderWatcher(OnFolderChanged);
        _hoverTimer = new Timer(_ => OnHoverElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _driveNotifier.Notified += OnDrivesChanged;
    }

    private float DpiScale => Dpi.width / _defaultDpi;

    // scales every drawn thing, text, rows, icons and thumbnails alike.
    public float Zoom
    {
        get => _zoom;
        set => _zoom = Math.Clamp(value, _minZoom, _maxZoom);
    }

    public ViewMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;

            _mode = value;
            _grid.Mode = value;
            _titleBar.Slider.Mode = value;
            View.Items = _items;
            View.Reset();
        }
    }

    protected override Icon? LoadCreationIcon() => Icon.LoadApplicationIcon(32);

    // the base constructor creates the window, and the device with it, so this has to be a property override.
    // assigning the flag in the constructor body would happen long after the device was built without it.
    // Direct2D refuses to sit on a D3D11 device created without BGRA support, and says only E_INVALIDARG about it.
    protected override D3D11_CREATE_DEVICE_FLAG DeviceCreateFlags
    {
        get => base.DeviceCreateFlags | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        set => base.DeviceCreateFlags = value;
    }

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

        _resources = new RenderResources(_deviceContext, DpiScale, _zoom);
        _images ??= new ImageCache(OnImageReady);
    }

    // called from a shell worker thread. InvalidateRect only posts, so this is safe from anywhere.
    private void OnImageReady() => Invalidate();

    protected override void DisposeDeviceDependentResources()
    {
        _images?.ClearBitmaps();
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

        // a monitor change or a zoom change means new metrics, the text formats are sized in pixels.
        if (Math.Abs(resources.DpiScale - DpiScale * _zoom) > float.Epsilon)
        {
            resources.Dispose();
            resources = new RenderResources(context, DpiScale, _zoom);
            _resources = resources;
        }

        _counters.BeginFrame();

        var client = ClientRect;
        var bounds = new D2D_RECT_F { left = 0, top = 0, right = client.Width, bottom = client.Height };
        _titleBar.IsMaximized = IsZoomed;
        _titleBar.Update(bounds, DpiScale);
        _titleBar.BackEnabled = _back.Count > 0;
        _titleBar.ForwardEnabled = _forward.Count > 0;
        _titleBar.UpEnabled = !string.IsNullOrEmpty(Path.GetDirectoryName(_path));
        Layout(DpiScale, bounds);

        var view = View;
        view.Items = _items;
        _counters.ItemCount = _items.Count;
        _counters.BufferBytes = _items.BufferBytes;

        // finished shell pixels become device bitmaps here, on the thread that owns the device.
        _images?.Upload(context);

        context.BeginDraw();
        context.Clear(Theme.Background);
        _drives.Render(context, resources);
        context.FillRectangle(_splitterBounds, _splitterHot || _splitterDragging ? resources.SplitterHotBrush : resources.SplitterBrush);
        if (_images != null)
        {
            view.Render(context, resources, _images, _path);
        }

        _titleBar.Render(context, resources, _titleText);
        if (_images != null)
        {
            _preview.Render(context, resources, _images, _items, _path, bounds);
        }

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

    private void Layout(float scale, in D2D_RECT_F bounds)
    {
        var splitter = MathF.Round(_splitterWidth * scale);
        var top = bounds.top + _titleBar.Height;
        var minimum = _minPaneWidth * scale;
        var maximum = MathF.Max(minimum, bounds.right - splitter - _minListWidth * scale);
        var width = Math.Clamp(MathF.Round(_paneWidth * scale), minimum, maximum);

        _drives.Bounds = new D2D_RECT_F { left = bounds.left, top = top, right = width, bottom = bounds.bottom };
        _splitterBounds = new D2D_RECT_F { left = width, top = top, right = width + splitter, bottom = bounds.bottom };
        var list = new D2D_RECT_F { left = width + splitter, top = top, right = bounds.right, bottom = bounds.bottom };
        _details.Bounds = list;
        _grid.Bounds = list;
    }

    private static bool Contains(in D2D_RECT_F rect, float x, float y) => x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;

    private static int FrameThickness =>
        Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXFRAME) + Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER);

    // the borders left as non client answer for themselves, only the top edge and the caption are ours.
    private LRESULT HitTest(HWND hwnd, WPARAM wParam, LPARAM lParam)
    {
        var result = DefWindowProc(hwnd, MessageDecoder.WM_NCHITTEST, wParam, lParam);
        if (result.Value != TitleBar.HitClient)
            return result;

        var point = new POINT { x = (short)(lParam.Value & 0xFFFF), y = (short)((lParam.Value >> 16) & 0xFFFF) };
        Functions.ScreenToClient(hwnd, ref point);
        if (!Functions.IsZoomed(hwnd) && point.y < FrameThickness)
            return new LRESULT { Value = TitleBar.HitTop };

        return new LRESULT { Value = _titleBar.HitTest(point.x, point.y) };
    }

    private void SetHotButton(int hitTest)
    {
        var hot = hitTest is TitleBar.HitMinimize or TitleBar.HitMaximize or TitleBar.HitClose ? hitTest : 0;
        if (_titleBar.HotButton == hot)
            return;

        _titleBar.HotButton = hot;
        RenderNow();
    }

    private void RenderNow()
    {
        RenderCore();
        Validate();
    }

    public void LoadDrives()
    {
        _ = LoadDrivesAsync();
        _ = _driveNotifier.Start();
    }

    // the shell reports a drive appearing or going away from its own thread.
    private void OnDrivesChanged(object? sender, ShellN.Extensions.ChangeNotifyEventArgs e)
    {
        try
        {
            _ = RunTaskOnUIThread(() =>
            {
                _ = LoadDrivesAsync();

                // the listing may have been sitting on the drive that just went away.
                if (!Directory.Exists(_path))
                {
                    Navigate(_path);
                }
            });
        }
        catch
        {
            // the window is on its way out.
        }
    }

    private async Task LoadDrivesAsync()
    {
        // a drive arriving while the previous listing is still coming in would otherwise leave the pane with both.
        var previous = _driveScan;
        if (previous != null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        var scan = new CancellationTokenSource();
        _driveScan = scan;
        _drives.Clear();

        try
        {
            await foreach (var drive in DriveScanner.ScanAsync(scan.Token).ConfigureAwait(true))
            {
                if (scan.IsCancellationRequested)
                    return;

                _drives.Add(drive);
                _drives.SyncTo(_path);
                RenderNow();
            }
        }
        catch (OperationCanceledException)
        {
            // a newer listing took over.
        }
    }

    public void Navigate(string path) => _ = NavigateAsync(path);

    private void NavigateFrom(string path)
    {
        if (!string.IsNullOrEmpty(_path) && !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(_path);
            _forward.Clear();
        }

        Navigate(path);
    }

    private void GoBack()
    {
        if (_back.Count == 0)
            return;

        _forward.Push(_path);
        Navigate(_back.Pop());
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
            return;

        _back.Push(_path);
        Navigate(_forward.Pop());
    }

    // the watcher runs on its own thread, and everything it touches belongs to the UI one.
    // this is a timer callback, so anything thrown here would take the process with it rather than surface.
    private void OnFolderChanged()
    {
        try
        {
            _ = RunTaskOnUIThread(Refresh);
        }
        catch
        {
            // the window is on its way out, and a listing that no longer refreshes is not worth a crash.
        }
    }

    private void OnNavigationButton(NavigationButton button)
    {
        switch (button)
        {
            case NavigationButton.Back:
                GoBack();
                break;

            case NavigationButton.Forward:
                GoForward();
                break;

            case NavigationButton.Up:
                NavigateUp();
                break;

            case NavigationButton.Reveal:
                RevealInExplorer();
                break;

            case NavigationButton.Hidden:
                // the enumerator does the filtering, so the folder has to be read again either way.
                _showHidden = !_showHidden;
                _titleBar.ShowHidden = _showHidden;
                Refresh();
                break;
        }
    }

    // opens Explorer on the current folder, with the selected item picked out when there is one.
    private unsafe void RevealInExplorer()
    {
        using var folder = ShellItems.Parse(_path, true);
        using var folderList = folder?.GetIdList(false);
        if (folderList is null)
            return;

        var position = View.SelectedPosition;
        if (position < 0 || position >= _items.Count)
        {
            ShellN.Functions.SHOpenFolderAndSelectItems(folderList.Pointer, 0, 0, 0);
            return;
        }

        ref readonly var entry = ref _items.EntryAt(position);
        using var item = ShellItems.Parse(Path.Join(_path, _items.NameOf(entry)), entry.IsDirectory);
        using var itemList = item?.GetIdList(false);
        if (itemList is null)
        {
            ShellN.Functions.SHOpenFolderAndSelectItems(folderList.Pointer, 0, 0, 0);
            return;
        }

        var pointer = itemList.Pointer;
        ShellN.Functions.SHOpenFolderAndSelectItems(folderList.Pointer, 1, (nint)(&pointer), 0);
    }

    private static string FirstExisting(string path, string fallback)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
                return current;

            current = Path.GetDirectoryName(current);
        }

        return fallback;
    }

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

        // the folder may have gone since it was last shown, taken with a deleted directory or an unplugged drive.
        path = FirstExisting(path, _defaultPath);
        _path = path;
        _titleText = path;
        Text = _title + " - " + path;
        _items.Reset();
        View.Reset();
        _drives.SyncTo(path);
        _preview.Hide();
        _images?.OnNavigate();
        _watcher.Watch(path);
        _counters.ScanMilliseconds = 0;
        _counters.SortMilliseconds = 0;
        _counters.FirstRowsMilliseconds = 0;

        var start = Stopwatch.GetTimestamp();
        var allocated = GC.GetTotalAllocatedBytes(false);
        var firstBatch = true;

        try
        {
            await foreach (var count in DirectoryScanner.ScanAsync(path, _items, _showHidden, scan.Token).ConfigureAwait(true))
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
            _titleText = path + "  " + ex.Message;
            Text = _title + " - " + path + " - " + ex.Message;
            return;
        }

        _counters.ScanMilliseconds = MillisecondsSince(start);
        _counters.ScanAllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated;

        SortItems(_items.SortColumn, _items.SortDescending);

        if (_restoreSelection >= 0)
        {
            View.Select(_restoreSelection);
            _restoreSelection = -1;
        }

        if (_restoreScroll > 0)
        {
            View.ScrollOffset = _restoreScroll;
            _restoreScroll = 0;
        }

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
        var path = Path.Join(_path, _items.NameOf(entry));
        if (entry.IsDirectory)
        {
            NavigateFrom(path);
            return;
        }

        Launch(path);
    }

    // runs the same command Explorer would, and does it off the UI thread because launching an application can
    // take a while and can put up UI of its own. only the path crosses the thread, the shell item is built there.
    private void Launch(string path)
    {
        var owner = Handle;
        _ = Task.Run(() =>
        {
            try
            {
                using var item = ShellItems.Parse(path, false);
                item?.InvokeDefaultCommand(owner, false);
            }
            catch
            {
                // a file can have no handler at all, or one that refuses, and neither is worth a crash.
            }
        });
    }

    private void NavigateUp()
    {
        var parent = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(parent))
            return;

        NavigateFrom(parent);
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // a shell context menu draws its own owner drawn items, and only if these reach it.
        if (msg == MessageDecoder.WM_INITMENUPOPUP ||
            msg == MessageDecoder.WM_MENUSELECT ||
            msg == MessageDecoder.WM_DRAWITEM ||
            msg == MessageDecoder.WM_MEASUREITEM ||
            msg == MessageDecoder.WM_MENUCHAR)
        {
            if (ShellN.Extensions.ShellItem.OnContextMenuWindowMessage(Handle, msg, wParam, lParam, out var menuResult).IsSuccess)
                return menuResult;
        }

        switch (msg)
        {
            case MessageDecoder.WM_MOUSEWHEEL:
                OnMouseWheel((short)((wParam.Value >> _wheelDeltaShift) & 0xFFFF));
                return new LRESULT();

            case MessageDecoder.WM_MOUSEMOVE:
                OnMouseMove(LowWord(lParam), HighWord(lParam));
                return new LRESULT();

            case MessageDecoder.WM_LBUTTONDOWN:
                SetFocus();
                OnMouseDown(LowWord(lParam), HighWord(lParam), false);
                return new LRESULT();

            case MessageDecoder.WM_CONTEXTMENU:
                OnContextMenu(lParam);
                return new LRESULT();

            case MessageDecoder.WM_LBUTTONDBLCLK:
                OnMouseDown(LowWord(lParam), HighWord(lParam), true);
                return new LRESULT();

            case MessageDecoder.WM_LBUTTONUP:
                if (_splitterDragging || View.ScrollbarDragging || _titleBar.Slider.Dragging)
                {
                    _splitterDragging = false;
                    View.EndScrollbarDrag();
                    _titleBar.Slider.EndDrag();
                    Functions.ReleaseCapture();
                    RenderNow();
                }
                return new LRESULT();

            // taking the whole window as client area is what removes the standard caption.
            // the frame is kept on the other three sides so resizing there still behaves normally.
            case MessageDecoder.WM_NCCALCSIZE:
                if (wParam.Value != 0)
                {
                    unsafe
                    {
                        var parameters = (NCCALCSIZE_PARAMS*)lParam.Value;
                        ref var rect = ref parameters->rgrc[0];
                        rect.left += FrameThickness;
                        rect.right -= FrameThickness;
                        rect.bottom -= FrameThickness;

                        // a maximised window would otherwise put its top edge past the screen.
                        if (Functions.IsZoomed(hwnd))
                        {
                            rect.top += FrameThickness;
                        }
                    }
                    return new LRESULT();
                }
                break;

            case MessageDecoder.WM_NCHITTEST:
                return HitTest(hwnd, wParam, lParam);

            case MessageDecoder.WM_NCMOUSEMOVE:
                SetHotButton((int)wParam.Value);
                break;

            case MessageDecoder.WM_NCMOUSELEAVE:
                SetHotButton(0);
                break;

            case MessageDecoder.WM_NCLBUTTONDOWN:
                switch ((int)wParam.Value)
                {
                    case TitleBar.HitMinimize:
                        Show(SHOW_WINDOW_CMD.SW_MINIMIZE);
                        return new LRESULT();

                    case TitleBar.HitMaximize:
                        Show(Functions.IsZoomed(hwnd) ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_MAXIMIZE);
                        return new LRESULT();

                    case TitleBar.HitClose:
                        Close();
                        return new LRESULT();
                }
                break;

            case MessageDecoder.WM_SETCURSOR:
                if ((_splitterHot || _splitterDragging) && (lParam.Value & 0xFFFF) == _hitTestClient)
                {
                    Functions.SetCursor(_sizeWECursor);
                    return new LRESULT { Value = 1 };
                }
                break;

            case MessageDecoder.WM_KEYDOWN:
                if (OnKeyDown((VIRTUAL_KEY)wParam.Value))
                    return new LRESULT();

                break;
        }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }

    private void OnMouseWheel(int delta)
    {
        DismissPreview();
        if (Functions.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0)
        {
            Zoom = delta > 0 ? _zoom * _zoomStep : _zoom / _zoomStep;
            RenderNow();
            return;
        }

        if (_drives.IndexAt(_lastMouseX, _lastMouseY) >= 0 || Contains(_drives.Bounds, _lastMouseX, _lastMouseY))
        {
            _drives.ScrollByWheel(delta);
        }
        else
        {
            View.ScrollByWheel(delta);
        }

        RenderNow();
    }

    private void OnMouseMove(float x, float y)
    {
        _lastMouseX = x;
        _lastMouseY = y;
        SetHotButton(0);

        if (_splitterDragging)
        {
            _paneWidth = (x - _splitterGrabOffset) / DpiScale;
            RenderNow();
            return;
        }

        if (_titleBar.Slider.Dragging)
        {
            _titleBar.Slider.Drag(x);
            RenderNow();
            return;
        }

        if (View.ScrollbarDragging)
        {
            View.DragScrollbar(y);
            RenderNow();
            return;
        }

        var changed = _titleBar.SetNavigationHover(x, y);
        changed |= _titleBar.SetSliderHover(x, y);
        var hot = Contains(_splitterBounds, x, y);
        if (hot != _splitterHot)
        {
            _splitterHot = hot;
            changed = true;
        }

        var before = View.HoverPosition;
        changed |= View.SetHover(x, y);
        changed |= _drives.SetHover(x, y);

        if (View.HoverPosition != before)
        {
            changed |= _preview.Hide();
            _hoverTimer.Change(_hoverPreviewDelay, Timeout.Infinite);
        }
        if (changed)
        {
            RenderNow();
        }
    }

    // the preview only survives while the pointer stays on the one image it belongs to.
    private void DismissPreview()
    {
        _hoverTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_preview.Hide())
        {
            RenderNow();
        }
    }

    private void OnHoverElapsed()
    {
        try
        {
            _ = RunTaskOnUIThread(ShowPreview);
        }
        catch
        {
            // the window is on its way out.
        }
    }

    private void ShowPreview()
    {
        var position = View.HoverPosition;
        if (position < 0 || position >= _items.Count)
            return;

        ref readonly var entry = ref _items.EntryAt(position);
        if (entry.IsDirectory || !ImageExtensions.CanDecode(_items.ExtensionOf(entry)))
            return;

        _preview.Show(position);
        RenderNow();
    }

    private void OnMouseDown(float x, float y, bool doubleClick)
    {
        DismissPreview();
        if (Contains(_splitterBounds, x, y))
        {
            _splitterDragging = true;
            _splitterGrabOffset = x - _splitterBounds.left;
            Functions.SetCapture(Handle);
            return;
        }

        if (_titleBar.Slider.BeginDrag(x, y))
        {
            Functions.SetCapture(Handle);
            RenderNow();
            return;
        }

        if (_titleBar.OnClick(x, y))
        {
            RenderNow();
            return;
        }

        if (View.BeginScrollbarDrag(x, y))
        {
            Functions.SetCapture(Handle);
            RenderNow();
            return;
        }

        if (_drives.OnClick(x, y))
        {
            RenderNow();
            return;
        }

        if (View.OnClick(x, y, doubleClick))
        {
            RenderNow();
        }
    }

    private void OnContextMenu(LPARAM lParam)
    {
        // the coordinates are on the screen here, and are -1 when the keyboard asked for the menu.
        var point = new POINT { x = (short)(lParam.Value & 0xFFFF), y = (short)((lParam.Value >> 16) & 0xFFFF) };
        int position;
        if (point.x == -1 && point.y == -1)
        {
            position = View.SelectedPosition;
        }
        else
        {
            Functions.ScreenToClient(Handle, ref point);
            position = View.PositionAtPoint(point.x, point.y);
        }

        if (position < 0 || position >= _items.Count)
            return;

        View.Select(position);
        RenderNow();

        ref readonly var entry = ref _items.EntryAt(position);
        var path = Path.Join(_path, _items.NameOf(entry));
        using var item = ShellItems.Parse(path, entry.IsDirectory);
        if (item == null)
            return;

        // a popup menu only tracks properly for a window that is in the foreground, and needs a message after it to close cleanly.
        // without these the menu appears but a click on it is lost.
        Functions.SetForegroundWindow(Handle);

        using var site = new ContextMenuSite(Handle);
        item.ShowContextMenu(site, flags: ShellN.CMF.CMF_EXPLORE | ShellN.CMF.CMF_EXTENDEDVERBS | ShellN.CMF.CMF_CANRENAME);

        Functions.PostMessageW(Handle, MessageDecoder.WM_NULL);

        // a command may have renamed or deleted something, so the listing is read again. the view keeps its place, which a plain navigation would throw away.
        Refresh();
    }

    private void Refresh()
    {
        _restoreScroll = View.ScrollOffset;
        _restoreSelection = View.SelectedPosition;
        Navigate(_path);
    }

    private bool OnKeyDown(VIRTUAL_KEY key)
    {
        var view = View;
        var page = view.PageSize;
        var step = view.Columns;
        switch (key)
        {
            case VIRTUAL_KEY.VK_UP:
                view.MoveSelection(-step);
                break;

            case VIRTUAL_KEY.VK_DOWN:
                view.MoveSelection(step);
                break;

            case VIRTUAL_KEY.VK_PRIOR:
                view.MoveSelection(-page);
                break;

            case VIRTUAL_KEY.VK_NEXT:
                view.MoveSelection(page);
                break;

            case VIRTUAL_KEY.VK_HOME:
                view.Select(0);
                break;

            case VIRTUAL_KEY.VK_END:
                view.Select(_items.Count - 1);
                break;

            case VIRTUAL_KEY.VK_RETURN:
                OnItemActivated(view.SelectedPosition);
                return true;

            case VIRTUAL_KEY.VK_BACK:
                NavigateUp();
                return true;

            case VIRTUAL_KEY.VK_ESCAPE:
                DismissPreview();
                return true;

            case VIRTUAL_KEY.VK_F5:
                Refresh();
                return true;

            case VIRTUAL_KEY.VK_F9:
                _continuous = !_continuous;
                break;

            // a full blocking compacting collection, so the overlay shows what a worst case pause would cost.
            case VIRTUAL_KEY.VK_F11:
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                break;

            case VIRTUAL_KEY.VK_F12:
                _overlay.Visible = !_overlay.Visible;
                break;

            default:
                // control with a digit picks the view, the way Explorer does it.
                if (Functions.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0 && key >= VIRTUAL_KEY.VK_1 && key <= VIRTUAL_KEY.VK_5)
                {
                    Mode = (ViewMode)(key - VIRTUAL_KEY.VK_1);
                    break;
                }

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
            _hoverTimer.Dispose();
            _watcher.Dispose();
            _driveNotifier.Notified -= OnDrivesChanged;
            _driveNotifier.Dispose();
            _driveScan?.Cancel();
            _driveScan?.Dispose();
            _driveScan = null;
            _images?.Dispose();
            _images = null;
            _items.Dispose();
        }
        base.Dispose(disposing);
    }
}
