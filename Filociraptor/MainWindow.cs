using ShellN.Extensions;

namespace Filociraptor;

internal sealed class MainWindow : D3D11SwapChainWindow
{
    private const string _title = "Filociraptor";
    private const string _positionArgument = "position";

    // a frame longer than this is treated as this long, the window having been idle rather than slow.
    private const float _maxFrameSeconds = 1f / 30;
    private const int _wheelDeltaShift = 16;
    private const int _hitTestClient = 1;
    private const int _sizeWECursorId = 32644;
    private const float _defaultPaneWidth = 220;
    private const float _minPaneWidth = 150;
    private const float _minListWidth = 260;
    private const float _splitterWidth = 6;
    private const int _hoverPreviewDelay = 300;

    private const int _saveQuietMilliseconds = 1000;
    private const double _minFontSize = 8;
    private const double _maxFontSize = 22;
    private const int _customColorCount = 16;
    private const double _maxPreviewPercent = 100;
    private const double _maxSpacingPercent = 400;

    private static readonly int[] _zoomStops = [50, 75, 100, 125, 150, 200, 300, 400];
    private const float _minZoom = 0.5f;
    private const float _maxZoom = 4;
    private const float _zoomStep = 1.1f;

    private readonly Settings _settings;
    private readonly FolderItems _items = new();
    private readonly DetailsView _details = new();
    private readonly GridView _grid = new();
    private readonly PlacesView _places = new();
    private readonly TitleBar _titleBar = new();
    private readonly SettingsMenu _menu = new();
    private readonly Stack<string> _back = [];
    private readonly Stack<string> _forward = [];
    private readonly ImagePreview _preview = new();
    private readonly Timer _hoverTimer;
    private readonly Timer _saveTimer;
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
    private bool _savePending;
    private int _appearance;
    private int _builtAppearance = -1;
    private float _restoreScroll;
    private int _restoreSelection = -1;

    private IItemsView View => _mode == ViewMode.Details ? _details : _grid;

    // topmost first, which is the order a message is offered in.
    private Control[] _controls = [];

    private void RebuildControls() => _controls = [_menu, _titleBar, (Control)View, _places];

    // every change comes through here, and the write happens once the changes stop. set while the window is being put where it belongs, before it is ever shown.
    internal bool IsPlacing { get; set; }

    // a modal control, the menu while it is up, is over everything and takes every message on its own.
    private Control? Modal
    {
        get
        {
            foreach (var control in _controls)
            {
                if (control.IsInteractive && control.IsModal)
                    return control;
            }

            return null;
        }
    }

    // hover is offered to all of them rather than stopping at the first, because a control clears its own hover when the pointer is no longer on it, and stopping early would leave the one behind lit.
    private bool RouteMouseMove(float x, float y)
    {
        var modal = Modal;
        if (modal != null)
            return modal.OnMouseMove(x, y);

        var changed = false;
        foreach (var control in _controls)
        {
            if (control.IsInteractive)
            {
                changed |= control.OnMouseMove(x, y);
            }
        }

        return changed;
    }

    private bool RouteMouseDown(float x, float y, bool doubleClick)
    {
        var modal = Modal;
        if (modal != null)
            return modal.OnMouseDown(x, y, doubleClick);

        foreach (var control in _controls)
        {
            if (control.IsInteractive && control.OnMouseDown(x, y, doubleClick))
            {
                if (control.IsCapturing)
                {
                    Functions.SetCapture(Handle);
                }

                return true;
            }
        }

        return false;
    }

    private bool RouteWheel(float x, float y, int delta)
    {
        var modal = Modal;
        if (modal != null)
            return modal.OnWheel(x, y, delta);

        foreach (var control in _controls)
        {
            if (control.IsInteractive && control.OnWheel(x, y, delta))
                return true;
        }

        return false;
    }

    private bool RouteMouseUp()
    {
        var released = false;
        foreach (var control in _controls)
        {
            released |= control.OnMouseUp();
        }

        return released;
    }

    public MainWindow(Settings settings)
        : base(_title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW)
    {
        _settings = settings;
        _zoom = Math.Clamp((float)settings.Zoom, _minZoom, _maxZoom);
        _grid.Settings = settings;
        _preview.Settings = settings;
        InvalidateOnTick = false;
        _details.ItemActivated = OnItemActivated;
        _details.SortRequested = OnSortRequested;
        _grid.ItemActivated = OnItemActivated;
        _places.DriveActivated = drive => Navigate(drive.Root);
        _places.PlaceActivated = OnPlaceActivated;
        _titleBar.NavigationPressed = OnNavigationButton;
        _titleBar.SettingsPressed = OnSettingsPressed;
        _titleBar.ZoomPressed = OnZoomPressed;
        _menu.Changed = OnSettingChanged;

        // whatever the menu put on screen goes away with it, the sample preview above being the one thing it does.
        _menu.Closed = DismissPreview;
        _titleBar.Slider.ModeChanged = mode => Mode = mode;
        _watcher = new FolderWatcher(OnFolderChanged);
        _namespaceWatcher = new NamespaceWatcher(OnFolderChanged);
        _hoverTimer = new Timer(_ => OnHoverElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer = new Timer(_ => OnSaveElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _driveNotifier.Notified += OnDrivesChanged;
        RebuildControls();
    }

    private float DpiScale => (float)Dpi.width / Constants.USER_DEFAULT_SCREEN_DPI;

    // scales every drawn thing, text, rows, icons and thumbnails alike.
    public float Zoom
    {
        get => _zoom;
        set
        {
            var zoom = Math.Clamp(value, _minZoom, _maxZoom);
            if (zoom == _zoom)
                return;

            _zoom = zoom;
            _settings.Zoom = zoom;
            ScheduleSave();
        }
    }

    public ViewMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;

            _mode = value;
            RebuildControls();
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

        _deviceContext.Object.SetDpi(Constants.USER_DEFAULT_SCREEN_DPI, Constants.USER_DEFAULT_SCREEN_DPI);

        _deviceContext.Object.SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE);

        _resources = new RenderResources(_deviceContext, DpiScale, _zoom, _settings);
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
            dpiX = Constants.USER_DEFAULT_SCREEN_DPI,
            dpiY = Constants.USER_DEFAULT_SCREEN_DPI,
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
        if (Math.Abs(resources.DpiScale - DpiScale * _zoom) > float.Epsilon || _appearance != _builtAppearance)
        {
            resources.Dispose();
            resources = new RenderResources(context, DpiScale, _zoom, _settings);
            _resources = resources;
            _builtAppearance = _appearance;
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

        // the plain dpi, no zoom. the caption is the window's own furniture and behaves like any other title bar, it follows the monitor.
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
        var moreImages = _images?.Upload(context) == true;

        context.BeginDraw();
        context.Clear(Theme.Background);
        _places.Render(context, resources, _images);
        context.FillRectangle(_splitterBounds, _splitterHot || _splitterDragging ? resources.SplitterHotBrush : resources.SplitterBrush);
        if (_images != null)
        {
            view.Render(context, resources, _images, _path, _location.HoldsStreams);
        }

        _titleBar.Render(context, resources, _titleText);
        if (_images != null)
        {
            _preview.Render(context, resources, _images, _items, _path, bounds);
        }

        _overlay.Render(context, resources, _counters, bounds);
        _menu.Render(context, resources);
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
        RememberLocation(location);
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
        if (entry.IsDirectory && !OpensAsFile(entry))
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

        // an archive is a file on disk and a folder to the shell, and here is where the two disagree.
        // Windows 11 browses one, so this browses it too rather than handing it to Explorer, unless the option asked for the file back.
        if (BrowsesAsFolder(entry) && TryBrowseInto(position))
            return;

        Launch(position);
    }

    private bool BrowsesAsFolder(in FileEntry entry) => ArchiveExtensions.ShownAsFolders && !_settings.OpenArchivesAsFiles && ArchiveExtensions.IsArchive(_items.ExtensionOf(entry));

    private bool TryBrowseInto(int position)
    {
        using var item = ItemFor(position);
        if (item == null || !ShellItems.IsFolder(item))
            return false;

        var target = ShellLocation.From(item);
        if (target == null)
            return false;

        NavigateFrom(target);
        return true;
    }

    // Windows 11 hands out an archive as a folder, and this is the option that asks for the file back.
    private bool OpensAsFile(in FileEntry entry) => _settings.OpenArchivesAsFiles && ArchiveExtensions.IsArchive(_items.ExtensionOf(entry));

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
                if (RouteMouseUp() | _splitterDragging)
                {
                    _splitterDragging = false;
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

            case MessageDecoder.WM_EXITSIZEMOVE:
                ScheduleSave();
                break;

            // clicking another application, or anything else that takes activation away, closes it too.
            case MessageDecoder.WM_ACTIVATE:
                if ((wParam.Value & 0xFFFF) == 0)
                {
                    DismissMenu();
                }
                break;

            case MessageDecoder.WM_NCLBUTTONDOWN:
            case MessageDecoder.WM_NCRBUTTONDOWN:
                DismissMenu();
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

            // per monitor v2, so a move to a screen with other scaling arrives here with the rectangle the window should take.
            case MessageDecoder.WM_DPICHANGED:
                if (!IsPlacing)
                {
                    unsafe
                    {
                        var suggested = *(RECT*)lParam.Value;
                        Functions.SetWindowPos(
                            hwnd,
                            HWND.Null,
                            suggested.left,
                            suggested.top,
                            suggested.Width,
                            suggested.Height,
                            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                    }
                }

                DismissMenu();
                ScheduleSave();
                return new LRESULT();

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
        if (Modal != null)
        {
            RouteWheel(_lastMouseX, _lastMouseY, delta);
            RenderNow();
            return;
        }

        DismissPreview();
        if (Functions.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0)
        {
            Zoom = delta > 0 ? _zoom * _zoomStep : _zoom / _zoomStep;
            RenderNow();
            return;
        }

        // whichever list is under the pointer takes it, and the listing takes it when none is.
        if (!RouteWheel(_lastMouseX, _lastMouseY, delta))
        {
            ((Control)View).OnWheel(View.Bounds.left + 1, View.Bounds.top + 1, delta);
        }

        RenderNow();
    }

    private void OnMouseMove(float x, float y)
    {
        _lastMouseX = x;
        _lastMouseY = y;
        SetHotButton(0);

        // the splitter belongs to the window rather than to any control, it is the gap between two of them.
        if (_splitterDragging)
        {
            _paneWidth = (x - _splitterGrabOffset) / DpiScale;
            RenderNow();
            return;
        }

        var before = View.HoverPosition;
        var changed = RouteMouseMove(x, y);

        var hot = Contains(_splitterBounds, x, y);
        if (hot != _splitterHot)
        {
            _splitterHot = hot;
            changed = true;
        }

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

    // the first image in the listing, shown while the preview size is being chosen so the slider has something to act on.
    // without it the slider moves against nothing, the menu holds the mouse so no file can be hovered.
    private void ShowPreviewSample()
    {
        if (!_preview.IsEnabled)
        {
            _preview.Hide();
            return;
        }

        if (_preview.Visible)
            return;

        if (!_location.IsFileSystem)
            return;

        for (var position = 0; position < _items.Count; position++)
        {
            ref readonly var entry = ref _items.EntryAt(position);
            if (!entry.IsDirectory && ImageExtensions.CanDecode(_items.ExtensionOf(entry)))
            {
                _preview.Show(position);
                return;
            }
        }
    }

    private void ShowPreview()
    {
        var position = View.HoverPosition;
        if (position < 0 || position >= _items.Count)
            return;

        if (!_preview.IsEnabled)
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

        if (RouteMouseDown(x, y, doubleClick))
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
        var invoked = false;
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

        if (invoked)
        {
            var requested = site.NavigateToParsingName;
            if (requested != null)
            {
                NavigateFrom(requested, verb.EqualsIgnoreCase("opennewprocess"));
                return;
            }
        }

        // a command may have renamed or deleted something, so the listing is read again. the view keeps its place, which a plain navigation would throw away.
        Functions.SetForegroundWindow(Handle);
        //Refresh();

        HRESULT getVerb(ShellN.IContextMenu cm, HWND hwnd, uint id)
        {
            invoked = true;
            site.NavigateToParsingName = null;
            verb = MenuItem.GetCommandString(cm, id);
            return ShellItem.Invoke(cm, hwnd, id);
        }
    }

    // the folder is kept so the next run opens on it, and so the recent list has something to show.
    private void RememberLocation(ShellLocation location)
    {
        var name = location.ParsingName;
        if (name.Length == 0)
            return;

        SettingsFile.RememberFolder(_settings, name, location.DisplayName);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _savePending = true;
        _saveTimer.Change(_saveQuietMilliseconds, Timeout.Infinite);
    }

    // the timer runs on its own thread, and everything it touches belongs to the UI one, so it only asks.
    private void OnSaveElapsed()
    {
        try
        {
            _ = RunTaskOnUIThread(SaveSettings);
        }
        catch (Exception ex)
        {
            // the window is on its way out, and Dispose writes the settings anyway.
            Application.TraceVerbose($"a settings save was dropped: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        if (!_savePending)
            return;

        _savePending = false;
        CapturePosition();
        SettingsFile.SaveLater(_settings);
    }

    // on the way out there is no later, so the file is written before the window goes.
    private void SaveSettingsNow()
    {
        _savePending = false;
        CapturePosition();
        SettingsFile.Save(_settings);
    }

    // read at the moment of writing rather than tracked as it moves, a window is moved and sized constantly and
    // only the last of it matters.
    private void CapturePosition()
    {
        var position = WindowPosition.Get(this);
        if (position != null)
        {
            _settings.Window = position.Value.ToString();
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
        var modal = Modal;
        if (modal != null && modal.OnKeyDown(key))
        {
            RenderNow();
            return true;
        }

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

    // the menu is dismissed from several places, a click outside it, the caption, losing activation, so it is
    // one call rather than the same three lines each time.
    private void DismissMenu()
    {
        if (!_menu.IsOpen)
            return;

        _menu.Close();
        RenderNow();
    }

    // the same menu the gear uses, with the sizes anyone would want, hung under the zoom rather than the gear.
    private void OnZoomPressed()
    {
        if (_menu.IsOpen)
        {
            DismissMenu();
            return;
        }

        var resources = _resources;
        if (resources == null)
            return;

        var client = ClientRect;
        var frame = new D2D_RECT_F { left = 0, top = 0, right = client.Width, bottom = client.Height };
        _menu.Open(BuildZoomEntries(), _titleBar.ZoomBounds, frame, resources);
        RenderNow();
    }

    private List<MenuEntry> BuildZoomEntries()
    {
        var entries = new List<MenuEntry>();
        foreach (var percent in _zoomStops)
        {
            var chosen = percent;
            entries.Add(new MenuEntry
            {
                Label = chosen + " %",
                Kind = MenuEntryKind.Toggle,
                Checked = () => MathF.Abs(_zoom * 100 - chosen) < 1,
                Invoked = () => Zoom = chosen / 100f,
            });
        }

        return entries;
    }

    private void OnSettingsPressed()
    {
        if (_menu.IsOpen)
        {
            _menu.Close();
            RenderNow();
            return;
        }

        var resources = _resources;
        if (resources == null)
            return;

        var client = ClientRect;
        var frame = new D2D_RECT_F { left = 0, top = 0, right = client.Width, bottom = client.Height };
        _menu.Open(BuildSettingsEntries(), _titleBar.GearBounds, frame, resources);
        RenderNow();
    }

    private void OnSettingChanged()
    {
        _appearance++;
        ScheduleSave();
        RenderNow();
    }

    private static MenuEntry Toggle(string label, Func<bool> get, Action<bool> set) => new()
    {
        Label = label,
        Kind = MenuEntryKind.Toggle,
        Checked = get,
        Invoked = () => set(!get()),
    };

    private IReadOnlyList<MenuEntry> BuildSettingsEntries() =>
    [
        new MenuEntry
        {
            Label = Res.SettingFont,
            Kind = MenuEntryKind.Choice,
            Value = () => _settings.FontFamily,
            Children = FontEntries,
        },
        new MenuEntry
        {
            Label = Res.SettingFontSize,
            Kind = MenuEntryKind.Slider,
            Minimum = _minFontSize,
            Maximum = _maxFontSize,
            Step = 0.5,
            Number = () => _settings.FontSize,
            SetNumber = value => _settings.FontSize = value,
            Value = () => _settings.FontSize.ToString("0.#"),
        },
        new MenuEntry
        {
            Label = Res.SettingTextColor,
            Kind = MenuEntryKind.Color,
            Value = () => _settings.TextColor,
            Invoked = PickTextColor,
        },
        new MenuEntry
        {
            Label = Res.SettingThumbnailSpacing,
            Kind = MenuEntryKind.Slider,
            Minimum = 0,
            Maximum = _maxSpacingPercent,
            Step = 10,
            Number = () => _settings.CellSpacingPercent,
            SetNumber = value => _settings.CellSpacingPercent = value,
            Value = () => _settings.CellSpacingPercent.ToString("0") + " %",
        },
        new MenuEntry
        {
            Label = Res.SettingImagePreview,
            Kind = MenuEntryKind.Slider,
            Minimum = 0,
            Maximum = _maxPreviewPercent,
            Step = 5,
            Number = () => _settings.PreviewPercent,
            SetNumber = value =>
            {
                _settings.PreviewPercent = value;
                ShowPreviewSample();
            },
            Value = () => _settings.PreviewPercent <= 0 ? Res.SettingOff : _settings.PreviewPercent.ToString("0") + " %",
        },
        MenuEntry.Separator,
        Toggle(Res.SettingSquareThumbnails, () => _settings.SquareThumbnails, value => _settings.SquareThumbnails = value),
        Toggle(Res.SettingThumbnailTitles, () => _settings.ThumbnailTitles, value => _settings.ThumbnailTitles = value),
        Toggle(Res.SettingWrapThumbnailTitles, () => _settings.WrapThumbnailTitles, value => _settings.WrapThumbnailTitles = value),
        new MenuEntry
        {
            Label = Res.SettingOpenArchivesAsFiles,
            Kind = MenuEntryKind.Toggle,
            Checked = () => _settings.OpenArchivesAsFiles,
            Invoked = () => _settings.OpenArchivesAsFiles = !_settings.OpenArchivesAsFiles,
            Enabled = () => ArchiveExtensions.ShownAsFolders,
        },
        MenuEntry.Separator,
        new MenuEntry
        {
            Label = Res.SettingRecentFolders,
            Kind = MenuEntryKind.Submenu,
            Children = RecentEntries,
        },
        MenuEntry.Separator,
        new MenuEntry
        {
            Label = Res.SettingSettingsFile,
            Kind = MenuEntryKind.Command,
            Value = () => SettingsFile.IsPortable ? Res.SettingPortable : Res.SettingRoaming,
            Invoked = RevealSettingsFile,
            ClosesMenu = true,
        },
    ];

    private IReadOnlyList<MenuEntry> FontEntries()
    {
        var names = new List<string>();
        try
        {
            using var factory = DWriteFunctions.DWriteCreateFactory();
            using var collection = factory.GetSystemFontCollection();
            foreach (var family in collection.GetFamilies())
            {
                // the first name is the one for the current language, which is what belongs in a menu.
                var name = family.GetNames().FirstOrDefault()?.String;
                if (!string.IsNullOrEmpty(name) && !IsSymbolFamily(family))
                {
                    names.Add(name);
                }

                family.Dispose();
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"the installed fonts could not be listed: {ex}");
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);

        var entries = new List<MenuEntry>();
        foreach (var name in names)
        {
            var chosen = name;
            entries.Add(new MenuEntry
            {
                Label = chosen,

                // each one drawn in itself, which says more about a font than its name does.
                PreviewFamily = chosen,
                Kind = MenuEntryKind.Toggle,
                Checked = () => _settings.FontFamily.EqualsIgnoreCase(chosen),
                Invoked = () => _settings.FontFamily = chosen,
            });
        }

        return entries;
    }

    private static bool IsSymbolFamily(IComObject<IDWriteFontFamily> family)
    {
        try
        {
            family.Object.GetFont(0, out var font).ThrowOnError();
            using var first = new ComObject<IDWriteFont>(font);
            return first.Object.IsSymbolFont();
        }
        catch
        {
            return false;
        }
    }

    // where the window has been, most recent first, with the ways of forgetting under them.
    private List<MenuEntry> RecentEntries()
    {
        var entries = new List<MenuEntry>();
        foreach (var folder in _settings.RecentFolders)
        {
            var target = folder.ParsingName;
            entries.Add(new MenuEntry
            {
                Label = folder.ToString(),
                Kind = MenuEntryKind.Command,
                ClosesMenu = true,
                Invoked = () => NavigateFrom(target),
            });
        }

        if (entries.Count == 0)
        {
            entries.Add(new MenuEntry { Label = Res.SettingNoRecentFolders, Kind = MenuEntryKind.Command, ClosesMenu = true });
            return entries;
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry
        {
            Label = Res.SettingRemoveMissingFolders,
            Kind = MenuEntryKind.Command,
            Invoked = () => _ = ForgetMissingFoldersAsync(),
        });

        entries.Add(new MenuEntry
        {
            Label = Res.SettingClearRecentFolders,
            Kind = MenuEntryKind.Command,
            Invoked = () => SettingsFile.ForgetAllFolders(_settings),
        });

        return entries;
    }

    // the menu stays up while the shell is asked about every folder in it, and the rows go when the answers are
    // in, which is the whole point of the command.
    private async Task ForgetMissingFoldersAsync()
    {
        if (!await SettingsFile.ForgetMissingFoldersAsync(_settings, ShellItems.Exists).ConfigureAwait(true))
            return;

        ScheduleSave();
        _menu.Refresh();
        RenderNow();
    }

    private unsafe void PickTextColor()
    {
        var current = _settings.Text;
        Span<uint> custom = stackalloc uint[_customColorCount];
        fixed (uint* colors = custom)
        {
            var choose = new CHOOSECOLORW
            {
                lStructSize = (uint)sizeof(CHOOSECOLORW),
                hwndOwner = Handle,
                lpCustColors = (nint)colors,
                rgbResult = new COLORREF((uint)(current.BR | (current.BG << 8) | (current.BB << 16))),
                Flags = CHOOSECOLOR_FLAGS.CC_RGBINIT | CHOOSECOLOR_FLAGS.CC_FULLOPEN,
            };

            if (!Functions.ChooseColorW(ref choose))
                return;

            var value = choose.rgbResult.Value;
            var picked = D3DCOLORVALUE.FromArgb(current.BA, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
            _settings.TextColor = picked.HtmlString;
        }
    }

    private void RevealSettingsFile()
    {
        var folder = Path.GetDirectoryName(SettingsFile.Location);
        if (folder == null)
            return;

        Directory.CreateDirectory(folder);
        NavigateFrom(folder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // written on the way out whether or not anything asked for it, because where the window ended up is
            // never something that asked.
            _saveTimer.Dispose();
            SaveSettingsNow();

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
