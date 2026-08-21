using ShellN.Extensions;

namespace Filociraptor;

internal sealed class MainWindow : D3D11SwapChainWindow
{
    private const string _title = "Filociraptor";
    private const string _positionArgument = "position";

    // a frame longer than this is treated as this long, the window having been idle rather than slow.
    private const float _maxFrameSeconds = 1f / 30;
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
    private readonly PlacesView _places = new();
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
    private readonly NamespaceWatcher _namespaceWatcher;
    private readonly DriveNotifier _driveNotifier = new();
    private ImageCache? _images;
    private CancellationTokenSource? _scan;
    private CancellationTokenSource? _driveScan;
    private static readonly string _defaultPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private ShellLocation _location = ShellLocation.ForPath(_defaultPath);
    private string _path = string.Empty;
    private string _titleText = _title;
    private bool _continuous;
    private ViewMode _mode = ViewMode.Details;
    private float _zoom = 1;
    private bool _showHidden;
    private bool _listed;
    private long _lastFrame;
    private bool _repaintOwed;
    private bool _imagesReady;
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
        _places.DriveActivated = drive => Navigate(drive.Root);
        _places.PlaceActivated = OnPlaceActivated;
        _titleBar.NavigationPressed = OnNavigationButton;
        _titleBar.Slider.ModeChanged = mode => Mode = mode;
        _watcher = new FolderWatcher(OnFolderChanged);
        _namespaceWatcher = new NamespaceWatcher(OnFolderChanged);
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
    private void OnImageReady()
    {
        Volatile.Write(ref _imagesReady, true);
        Invalidate();
    }

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

        // hover fades advance by the time the last frame took, so they run at the same speed whatever the frame rate.
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastFrame == 0 ? 0 : (float)((now - _lastFrame) / (double)Stopwatch.Frequency);
        _lastFrame = now;

        // a frame that arrives long after the previous one, the window having sat idle, must not jump a fade straight to its end.
        resources.ElapsedSeconds = MathF.Min(elapsed, _maxFrameSeconds);
        resources.Animating = false;

        // cleared before the upload below, so an image arriving during this frame still counts as owed rather than being swallowed by the frame that was already running.
        Volatile.Write(ref _imagesReady, false);

        var client = ClientRect;
        var bounds = new D2D_RECT_F { left = 0, top = 0, right = client.Width, bottom = client.Height };
        _titleBar.IsMaximized = IsZoomed;
        _titleBar.Update(bounds, DpiScale);
        _titleBar.BackEnabled = _back.Count > 0;
        _titleBar.ForwardEnabled = _forward.Count > 0;
        _titleBar.UpEnabled = _location.ParsingName.Length > 0;
        Layout(DpiScale, bounds);

        var view = View;
        view.Items = _items;
        _counters.ItemCount = _items.Count;
        _counters.BufferBytes = _items.BufferBytes;

        // finished shell pixels become device bitmaps here, on the thread that owns the device.
        // it uploads a bounded number per frame, so a backlog owes another frame to finish draining.
        var moreImages = _images?.Upload(context) == true;

        context.BeginDraw();
        context.Clear(Theme.Background);
        _places.Render(context, resources, _images);
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

        // something is still fading, so another frame is owed. when nothing is, the window goes quiet again.
        _repaintOwed = _continuous || resources.Animating || moreImages;
        swapChain.Present(_repaintOwed ? 1u : 0, 0);

        if (_repaintOwed)
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

        _places.Bounds = new D2D_RECT_F { left = bounds.left, top = top, right = width, bottom = bounds.bottom };
        _splitterBounds = new D2D_RECT_F { left = width, top = top, right = width + splitter, bottom = bounds.bottom };
        var list = new D2D_RECT_F { left = width + splitter, top = top, right = bounds.right, bottom = bounds.bottom };
        _details.Bounds = list;
        _grid.Bounds = list;
    }

    private static bool Contains(in D2D_RECT_F rect, float x, float y) => x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;

    private static int FrameThickness => Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXFRAME) + Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER);

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

        // an immediate frame makes any pending paint redundant, which is why the window is validated here.
        Validate();
        if (_repaintOwed || Volatile.Read(ref _imagesReady))
        {
            Invalidate();
        }
    }

    public void LoadDrives()
    {
        _ = LoadDrivesAsync();
        _ = LoadPlacesAsync();
        _ = _driveNotifier.Start();
    }

    // the places the shell offers, which unlike the drives do not come and go, so they are read once.
    private async Task LoadPlacesAsync()
    {
        var places = await PlacesScanner.ScanAsync(CancellationToken.None).ConfigureAwait(true);
        _places.SetPlaces(places);
        _places.SyncTo(_location);
        RenderNow();
    }

    // the shell reports a drive appearing or going away from its own thread.
    private void OnDrivesChanged(object? sender, ChangeNotifyEventArgs e)
    {
        try
        {
            _ = RunTaskOnUIThread(() =>
            {
                _ = LoadDrivesAsync();

                // the listing may have been sitting on the drive that just went away.
                if (_location.IsFileSystem && !Directory.Exists(_path))
                {
                    Navigate(_location.ParsingName);
                }
            });
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"a drive change was dropped: {ex.Message}");
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
        _places.Clear();

        try
        {
            await foreach (var drive in DriveScanner.ScanAsync(scan.Token).ConfigureAwait(true))
            {
                if (scan.IsCancellationRequested)
                    return;

                _places.Add(drive);
                _places.SyncTo(_location);
                RenderNow();
            }
        }
        catch (OperationCanceledException)
        {
            // continue
        }
    }

    public void Navigate(string path) => _ = NavigateAsync(path);

    // a place carries the id list it was enumerated with, so it opens through that rather than through a name the shell may refuse to parse back.
    private void OnPlaceActivated(PlaceEntry place)
    {
        using var item = ShellItems.Bind(place.IdList) ?? ShellItems.Parse(place.ParsingName, true);
        var location = item == null ? null : ShellLocation.From(item);
        if (location == null)
        {
            Application.TraceWarning($"'{place.ParsingName}' could not be opened.");
            return;
        }

        NavigateFrom(location);
    }

    private void NavigateFrom(string path, bool newProcess = false) => NavigateFrom(path, null, newProcess);

    private void NavigateFrom(ShellLocation location, bool newProcess = false) => NavigateFrom(location.ParsingName, location, newProcess);

    private void NavigateFrom(string path, ShellLocation? resolved, bool newProcess)
    {
        if (newProcess)
        {
            StartNewProcess(path);
            return;
        }

        var current = _location.ParsingName;
        if (!string.IsNullOrEmpty(current) && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(current);
            _forward.Clear();
        }

        _ = NavigateAsync(path, resolved);
    }

    // "open in new process" means what it says, another one of us rather than another folder in this one.
    // where this window sits goes with it, so the new one opens beside this one instead of exactly on top of it.
    private void StartNewProcess(string path)
    {
        var executable = Environment.ProcessPath;
        if (executable == null)
            return;

        var arguments = $"{Quote(path)} -{_positionArgument}:{WindowRect}";

        try
        {
            var info = new ProcessStartInfo { FileName = executable, Arguments = arguments, UseShellExecute = false };
            using var process = Process.Start(info);
        }
        catch (Exception ex)
        {
            Application.TraceError($"another instance could not be started on '{path}': {ex}");
        }
    }

    private static string Quote(string value)
    {
        var trailing = 0;
        while (trailing < value.Length && value[value.Length - 1 - trailing] == '\\')
        {
            trailing++;
        }

        return string.Concat("\"", value, new string('\\', trailing), "\"");
    }

    private void GoBack()
    {
        if (_back.Count == 0)
            return;

        _forward.Push(_location.ParsingName);
        Navigate(_back.Pop());
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
            return;

        _back.Push(_location.ParsingName);
        Navigate(_forward.Pop());
    }

    // the watcher runs on its own thread, and everything it touches belongs to the UI one.
    private void OnFolderChanged()
    {
        try
        {
            _ = RunTaskOnUIThread(Refresh);
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"a folder change was dropped: {ex.Message}");
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

    private unsafe void RevealInExplorer()
    {
        using var folder = _location.Bind();
        using var folderList = folder?.GetIdList(false);
        if (folderList is null)
            return;

        var position = View.SelectedPosition;
        if (position < 0 || position >= _items.Count)
        {
            ShellN.Functions.SHOpenFolderAndSelectItems(folderList.Pointer, 0, 0, 0);
            return;
        }

        using var item = ItemFor(position);
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

    private async Task NavigateAsync(string path, ShellLocation? resolved = null)
    {
        var location = resolved ?? ShellLocation.Resolve(path);
        if (location == null)
        {
            // a namespace name has no parent to walk up to, its segments are not directories
            if (ShellLocation.IsNamespaceName(path) && _listed)
            {
                Application.TraceWarning($"'{path}' could not be resolved, staying in '{_location.ParsingName}'.");
                return;
            }

            // a folder on disk may simply have gone, with a deleted directory or an unplugged drive, and then go to nearest parent
            var existing = FirstExisting(path, _defaultPath);
            Application.TraceWarning($"'{path}' could not be resolved, falling back to '{existing}'.");
            location = ShellLocation.Resolve(existing) ?? ShellLocation.ForPath(existing);
        }

        var previous = _scan;
        if (previous != null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        var scan = new CancellationTokenSource();
        _scan = scan;

        _listed = true;
        _location = location;
        _path = location.Path ?? string.Empty;
        _titleText = location.IsFileSystem ? location.Path! : location.DisplayName;
        Text = _title + " - " + _titleText;
        _items.Reset();
        View.Reset();
        _places.SyncTo(_location);
        _preview.Hide();
        _images?.OnNavigate();

        // a folder on disk is watched by the file system, anywhere else by the shell
        if (location.IsFileSystem)
        {
            _namespaceWatcher.Stop();
            _watcher.Watch(location.Path!);
        }
        else
        {
            _watcher.Stop();
            _namespaceWatcher.Watch(location);
        }
        _counters.ScanMilliseconds = 0;
        _counters.SortMilliseconds = 0;
        _counters.FirstRowsMilliseconds = 0;

        var start = Stopwatch.GetTimestamp();
        var allocated = GC.GetTotalAllocatedBytes(false);
        var firstBatch = true;

        try
        {
            var scanner = location.IsFileSystem ? DirectoryScanner.ScanAsync(location.Path!, _items, _showHidden, scan.Token) : NamespaceScanner.ScanAsync(location, _items, _showHidden, scan.Token);

            await foreach (var count in scanner.ConfigureAwait(true))
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
            // continue
            return;
        }
        catch (Exception ex)
        {
            Application.TraceError($"'{location.ParsingName}' could not be listed: {ex}");
            _titleText += "  " + ex.Message;
            Text = _title + " - " + _titleText + " - " + ex.Message;
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
        if (entry.IsDirectory)
        {
            using var item = ItemFor(position);
            var target = item == null ? null : ShellLocation.From(item);
            if (target == null)
            {
                Application.TraceWarning($"'{ParsingNameOf(entry)}' could not be opened.");
                return;
            }

            NavigateFrom(target);
            return;
        }

        Launch(position);
    }

    private ShellItem? ItemFor(int position)
    {
        var bound = ShellItems.Bind(_items.IdListAt(position));
        if (bound != null)
            return bound;

        ref readonly var entry = ref _items.EntryAt(position);
        return ShellItems.Parse(ParsingNameOf(entry), entry.IsDirectory);
    }

    private string ParsingNameOf(in FileEntry entry)
    {
        var parsing = _items.ParsingNameOf(entry);
        return parsing.Length > 0 ? parsing.ToString() : Path.Join(_location.Path ?? _path, _items.NameOf(entry));
    }

    // runs the same command Explorer would, and does it off the UI thread because launching an application can take a while and can put up UI of its own
    private void Launch(int position)
    {
        ref readonly var entry = ref _items.EntryAt(position);
        var path = ParsingNameOf(entry);

        // the id list points into the listing's arena, which the next navigation resets, so it is copied rather than handed to a thread that outlives the row.
        var idList = _items.IdListAt(position).ToArray();

        var owner = Handle;
        _ = Task.Run(() =>
        {
            try
            {
                using var item = ShellItems.Bind(idList) ?? ShellItems.Parse(path, false);
                item?.InvokeDefaultCommand(owner, false);
            }
            catch (Exception ex)
            {
                Application.TraceError($"'{path}' could not be launched: {ex}");
            }
        });
    }

    private void NavigateUp()
    {
        var parent = _location.GetParent();
        if (parent == null)
            return;

        NavigateFrom(parent.ParsingName);
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
            if (ShellItem.OnContextMenuWindowMessage(Handle, msg, wParam, lParam, out var menuResult).IsSuccess)
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

        if (_places.IndexAt(_lastMouseX, _lastMouseY) >= 0 || Contains(_places.Bounds, _lastMouseX, _lastMouseY))
        {
            _places.ScrollByWheel(delta);
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
        changed |= _places.SetHover(x, y);

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
        catch (Exception ex)
        {
            Application.TraceVerbose($"a hover preview was dropped: {ex.Message}");
        }
    }

    private void ShowPreview()
    {
        var position = View.HoverPosition;
        if (position < 0 || position >= _items.Count)
            return;

        ref readonly var entry = ref _items.EntryAt(position);
        if (entry.IsDirectory || !_location.IsFileSystem || !ImageExtensions.CanDecode(_items.ExtensionOf(entry)))
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

        if (_places.OnClick(x, y))
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

        var onItem = position >= 0 && position < _items.Count;
        if (onItem)
        {
            View.Select(position);
            RenderNow();
        }

        // an item's menu comes from the item, the menu for the empty space around it comes from the folder.
        using var target = onItem ? ItemFor(position) : _location.Bind();
        if (target == null)
            return;

        // see https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenu#remarks
        Functions.SetForegroundWindow(Handle);

        string? verb = null;
        var site = new ContextMenuSite(Handle);
        var flags = ShellN.CMF.CMF_EXPLORE | ShellN.CMF.CMF_EXTENDEDVERBS | ShellN.CMF.CMF_CANRENAME;
        if (onItem)
        {
            using var pidl = target.GetIdList(false);
            if (pidl is null)
                return;

            using var parent = target.GetParentIdList();
            if (parent is null)
                return;

            ShellItem.ShowContextMenu(parent, [pidl], site, flags: flags, invoke: getVerb);
        }
        else
        {
            target.ShowContextMenu(site, flags: flags, invoke: getVerb);
        }

        Functions.PostMessageW(Handle, MessageDecoder.WM_NULL);

        var requested = site.NavigateToParsingName;
        if (requested != null)
        {
            NavigateFrom(requested, verb.EqualsIgnoreCase("opennewprocess"));
            return;
        }

        // a command may have renamed or deleted something, so the listing is read again. the view keeps its place, which a plain navigation would throw away.
        Functions.SetForegroundWindow(Handle);
        Refresh();

        HRESULT getVerb(ShellN.IContextMenu cm, HWND hwnd, uint id)
        {
            verb = MenuItem.GetCommandString(cm, id);
            return ShellItem.Invoke(cm, hwnd, id);
        }
    }

    private void Refresh()
    {
        _restoreScroll = View.ScrollOffset;
        _restoreSelection = View.SelectedPosition;

        // the same folder as before
        _ = NavigateAsync(_location.ParsingName, _location);
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
            _namespaceWatcher.Dispose();
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
