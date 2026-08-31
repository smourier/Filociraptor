namespace Filociraptor.Shell;

// the UI thread side of the image pipeline. it decides what to ask for, holds the results as device bitmaps, and never blocks.
// lookups happen for every visible row on every frame, so they go through an alternate span lookup and allocate nothing on a hit.
internal sealed class ImageCache : IDisposable
{
    public const string DirectoryKey = "<dir>";
    public const string NoExtensionKey = "<none>";
    private const int _maxUploadsPerFrame = 24;
    private const int _maxKeyLength = 560;
    private const int _bytesPerPixel = 4;

    // the sizes the system keeps icons at.
    private static readonly int[] _standardSizes = [16, 20, 24, 32, 48, 64, 96, 128, 256, 384, 512, 768, 1024, 1536, 2048];

    // these carry their own icon, so sharing one per extension would show the wrong thing.
    private static readonly string[] _selfIconedExtensions = [".exe", ".lnk", ".ico", ".cur", ".ani", ".url", ".msc", ".scr", ".cpl"];

    private readonly Dictionary<string, IComObject<ID2D1Bitmap>> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _typeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _perFileKeys = [];
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShellImageLoader _loader;
    private string? _preview;

    private readonly Dictionary<string, IComObject<ID2D1Bitmap>>.AlternateLookup<ReadOnlySpan<char>> _bitmapLookup;
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _typeNameLookup;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _pendingLookup;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _failedLookup;

    public ImageCache(Action imageReady)
    {
        _loader = new ShellImageLoader(imageReady);
        _bitmapLookup = _bitmaps.GetAlternateLookup<ReadOnlySpan<char>>();
        _typeNameLookup = _typeNames.GetAlternateLookup<ReadOnlySpan<char>>();
        _pendingLookup = _pending.GetAlternateLookup<ReadOnlySpan<char>>();
        _failedLookup = _failed.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public static bool IsSelfIconed(ReadOnlySpan<char> extension)
    {
        foreach (var known in _selfIconedExtensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public IComObject<ID2D1Bitmap>? Get(ReadOnlySpan<char> key) => _bitmapLookup.TryGetValue(key, out var bitmap) ? bitmap : null;
    public string? GetTypeName(ReadOnlySpan<char> key) => _typeNameLookup.TryGetValue(key, out var name) ? name : null;

    private static bool CanShare(bool isDirectory, bool wantThumbnail, ReadOnlySpan<char> extension) => !wantThumbnail && (isDirectory || !IsSelfIconed(extension));

    // the one entry point the views use. it returns whatever is ready and queues whatever is not, never blocking.
    public static int StandardSize(int size)
    {
        foreach (var standard in _standardSizes)
        {
            if (standard >= size)
                return standard;
        }

        return _standardSizes[^1];
    }

    // parsingName is empty for a file on disk, whose identity is its folder and its name.
    // an item the namespace described carries one, because there is no folder to join it to.
    public IComObject<ID2D1Bitmap>? GetOrRequest(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> extension,
        bool isDirectory,
        string folderPath,
        int size,
        bool wantThumbnail,
        ReadOnlySpan<char> parsingName = default,
        bool keep = false,
        bool isStream = false)
    {
        size = StandardSize(size);
        Span<char> buffer = stackalloc char[_maxKeyLength];
        var key = new ScratchText(buffer);

        // a namespace item has nothing an extension could be shared on, so it is always asked for by itself.
        // inside an archive the shell offers the same blank page for everything, so the extension provides the icon the way it does for a file on disk, and only a picture we can decode ourselves is asked for by item.
        var streamThumbnail = isStream && wantThumbnail && !isDirectory && ImageExtensions.CanDecode(extension);
        var shared = isStream ? !streamThumbnail : parsingName.Length == 0 && CanShare(isDirectory, wantThumbnail, extension);
        if (shared)
        {
            AppendSharedKey(ref key, extension, isDirectory, size);
        }
        else if (parsingName.Length > 0)
        {
            key.Append(size);
            key.Append('|');
            key.Append(parsingName);
        }
        else
        {
            key.Append(size);
            key.Append('|');
            key.Append(folderPath);
            if (!folderPath.EndsWith('\\'))
            {
                key.Append('\\');
            }

            key.Append(name);
        }

        var bitmap = Get(key.Text);
        if (bitmap != null)
            return bitmap;

        if (shared)
        {
            // the loader turns this into a name for a file that does not exist, which is all the shell needs.
            var target = isDirectory || extension.Length == 0 ? string.Empty : extension.ToString();

            // the sample is a real file of that extension, for the types that register their icon as the file itself. inside an archive there is no path to one, but the parsing name reaches it.
            var sample = isDirectory ? null : isStream ? parsingName.ToString() : Path.Join(folderPath, name);
            Request(key.Text, target, ShellImageKind.ExtensionIcon, size, isDirectory, sample);
        }
        else
        {
            var kind = streamThumbnail ? ShellImageKind.StreamImage : wantThumbnail ? ShellImageKind.Thumbnail : ShellImageKind.FileIcon;
            var target = parsingName.Length > 0 ? parsingName.ToString() : Path.Join(folderPath, name);
            Request(key.Text, target, kind, size, isDirectory, keep: keep);
        }

        return null;
    }

    // the preview is decoded from the file by WIC rather than taken from the shell.
    public IComObject<ID2D1Bitmap>? GetOrRequestPreview(ReadOnlySpan<char> name, string folderPath, int size)
    {
        size = StandardSize(size);
        Span<char> buffer = stackalloc char[_maxKeyLength];
        var key = new ScratchText(buffer);
        key.Append("preview|");
        key.Append(size);
        key.Append('|');
        key.Append(folderPath);
        if (!folderPath.EndsWith(Path.DirectorySeparatorChar))
        {
            key.Append(Path.DirectorySeparatorChar);
        }

        key.Append(name);

        // a decoded picture is megabytes, not kilobytes like an icon, and only one is ever on screen. so the previous one goes as soon as a different one is wanted.
        if (_preview != null && !key.Text.SequenceEqual(_preview))
        {
            if (_bitmaps.Remove(_preview, out var previous))
            {
                previous.Dispose();
            }

            _pending.Remove(_preview);
            _perFileKeys.Remove(_preview);
            _preview = null;
        }

        var bitmap = Get(key.Text);
        if (bitmap != null)
            return bitmap;

        _preview = key.Text.ToString();
        Request(_preview, Path.Join(folderPath, name), ShellImageKind.Image, size, false);
        return null;
    }

    // the shell hands back the type name with the icon, so the type column costs nothing extra.
    public string? GetTypeNameFor(ReadOnlySpan<char> extension, bool isDirectory, int size)
    {
        Span<char> buffer = stackalloc char[64];
        var key = new ScratchText(buffer);
        AppendSharedKey(ref key, extension, isDirectory, size);
        return GetTypeName(key.Text);
    }

    private static void AppendSharedKey(ref ScratchText key, ReadOnlySpan<char> extension, bool isDirectory, int size)
    {
        key.Append(size);
        key.Append('|');
        key.Append(isDirectory ? DirectoryKey : extension.Length > 0 ? extension : NoExtensionKey);
    }

    public void Request(ReadOnlySpan<char> key, string target, ShellImageKind kind, int size, bool isDirectory, string? samplePath = null, bool keep = false)
    {
        if (_bitmapLookup.ContainsKey(key) || _pendingLookup.Contains(key) || _failedLookup.Contains(key))
            return;

        var text = key.ToString();
        _pending.Add(text);

        // the left pane is not the listing, so navigating must not throw its icons away, nor drop the ones still on their way.
        // they would come back a moment later and the pane would blink on every navigation.
        if (kind != ShellImageKind.ExtensionIcon && !keep)
        {
            _perFileKeys.Add(text);
        }

        _loader.Request(new ShellImageRequest
        {
            Key = text,
            Target = target,
            Size = size,
            Kind = kind,
            IsDirectory = isDirectory,
            Generation = keep ? ShellImageRequest.NeverStale : _loader.Generation,
            SamplePath = samplePath,
            CachedOnly = kind == ShellImageKind.Thumbnail,
        });
    }

    // turns finished pixels into device bitmaps, a bounded number per frame so a burst of arrivals cannot turn into a visible hitch.
    // returns true when it stopped at that bound rather than because the queue ran dry, which means another frame is owed, otherwise a backlog would wait for something else to happen to draw.
    public unsafe bool Upload(IComObject<ID2D1DeviceContext> deviceContext)
    {
        var generation = _loader.Generation;
        var uploaded = 0;
        for (; uploaded < _maxUploadsPerFrame && _loader.TryDequeue(out var image); uploaded++)
        {
            _pending.Remove(image.Key);
            if (image.Width == 0 || image.Height == 0)
            {
                _failed.Add(image.Key);
            }

            if ((image.Generation != ShellImageRequest.NeverStale && image.Generation != generation) || image.Width == 0 || image.Height == 0)
                continue;

            if (image.TypeName != null)
            {
                _typeNames[image.Key] = image.TypeName;
            }

            var properties = new D2D1_BITMAP_PROPERTIES1
            {
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                },
                dpiX = Constants.USER_DEFAULT_SCREEN_DPI,
                dpiY = Constants.USER_DEFAULT_SCREEN_DPI,
            };

            fixed (byte* pixels = image.Pixels)
            {
                var size = new D2D_SIZE_U { width = image.Width, height = image.Height };
                var bitmap = deviceContext.CreateBitmap(size, (nint)pixels, image.Width * _bytesPerPixel, properties);
                if (_bitmaps.TryGetValue(image.Key, out var existing))
                {
                    existing.Dispose();
                }

                _bitmaps[image.Key] = bitmap;
            }
        }

        return uploaded == _maxUploadsPerFrame;
    }

    // a new folder invalidates everything keyed by path.
    // the icons shared by extension stay, they are the same everywhere and they are what makes the next listing appear already populated.
    public void OnNavigate()
    {
        _loader.NextGeneration();
        foreach (var key in _perFileKeys)
        {
            if (_bitmaps.Remove(key, out var bitmap))
            {
                bitmap.Dispose();
            }

            _pending.Remove(key);
        }

        _perFileKeys.Clear();

        // reading a folder again is also how someone asks for another try at whatever had no icon.
        _failed.Clear();
        _preview = null;
    }

    // device bitmaps do not survive their device.
    public void ClearBitmaps()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            bitmap.Dispose();
        }

        _bitmaps.Clear();
        _pending.Clear();
        _perFileKeys.Clear();
        _preview = null;
    }

    public void Dispose()
    {
        _loader.Dispose();
        ClearBitmaps();
    }
}
