using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// every shell call in the application happens here, never on the UI thread.
// a thumbnail handler is third party code that can block for a long time on a network path or a placeholder file, so the number of calls in flight is capped and the UI never waits on any of them.
internal sealed class ShellImageLoader : IDisposable
{
    private const int _minWorkers = 2;
    private const int _maxAttempts = 3;

    private const int _maxWorkers = 4;
    private const int _bytesPerPixel = 4;
    private const string _syntheticName = "filociraptor";

    // a folder that certainly exists, so the parsing name resolves. the file itself never has to.
    private static readonly string _syntheticRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    // a stack, not a queue. scrolling fast leaves a backlog of rows nobody is looking at any more,
    // and the rows now on screen are the ones just asked for, so the newest request is always the one worth doing next.
    private readonly ConcurrentStack<ShellImageRequest> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentQueue<ShellImage> _results = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action _imageReady;
    private int _generation;
    private bool _disposed;

    public ShellImageLoader(Action imageReady)
    {
        _imageReady = imageReady;

        var count = Math.Clamp(Environment.ProcessorCount / 2, _minWorkers, _maxWorkers);
        for (var i = 0; i < count; i++)
        {
            _ = Task.Run(WorkAsync);
        }
    }

    public int Generation => Volatile.Read(ref _generation);

    // called when the listing changes. work queued for the previous folder is dropped rather than cancelled, because a shell call already in flight cannot be interrupted.
    public void NextGeneration() => Interlocked.Increment(ref _generation);

    public void Request(in ShellImageRequest request)
    {
        if (_disposed)
            return;

        Push(request);
    }

    public bool TryDequeue([NotNullWhen(true)] out ShellImage? image) => _results.TryDequeue(out image);

    private void Push(in ShellImageRequest request)
    {
        _pending.Push(request);
        _signal.Release();
    }

    private async Task WorkAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                await _signal.WaitAsync(_cancellation.Token).ConfigureAwait(false);
                if (!_pending.TryPop(out var request))
                    continue;

                if (request.Generation != ShellImageRequest.NeverStale && request.Generation != Generation)
                    continue;

                try
                {
                    var image = await LoadAsync(request).ConfigureAwait(false);
                    if (image != null)
                    {
                        _results.Enqueue(image);
                        _imageReady();

                        if (image.Provisional)
                        {
                            Push(request with { CachedOnly = false });
                        }
                    }
                    else if (request.CachedOnly)
                    {
                        // nothing cached, so ask for the real thing.
                        Push(request with { CachedOnly = false });
                    }
                    else if (request.Attempt + 1 < _maxAttempts)
                    {
                        // the shell answered with nothing, which it does now and then for an item it describes well later.
                        // asking again is what stops a row keeping an empty icon until the folder is read again.
                        Push(request with { Attempt = request.Attempt + 1 });
                    }
                    else
                    {
                        // out of tries, and the cache has to hear about it,
                        // or it will hold the request as pending and never ask for this one again.
                        _results.Enqueue(ShellImage.Nothing(request));
                        _imageReady();
                    }
                }
                catch (Exception ex)
                {
                    Application.TraceVerbose($"no {request.Kind} for '{request.Target}': {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // continue.
        }
    }

    private static Task<ShellImage?> LoadAsync(ShellImageRequest request) => request.Kind switch
    {
        ShellImageKind.ExtensionIcon => LoadExtensionIconAsync(request),
        ShellImageKind.Image => Task.FromResult(DecodeImage(request)),
        ShellImageKind.StreamImage => Task.FromResult(DecodeStreamImage(request)),
        _ => LoadShellImageAsync(request),
    };

    // WIC reads the file itself, so the preview is the real picture at the size asked for rather than whatever thumbnail the shell happens to hold.
    // it is slow enough to matter, which is why it happens here.
    private static ShellImage? DecodeImage(in ShellImageRequest request)
    {
        using var decoder = WicImagingFactory.CreateDecoderFromFilename(request.Target);
        return FirstFrameOf(request, decoder);
    }

    // there is no file to open inside an archive, so the shell is asked for the bytes instead and WIC reads those.
    // the shell has an icon for such an item and it is the same blank page for all of them, so the picture has to be made here or there is no picture at all.
    private static ShellImage? DecodeStreamImage(in ShellImageRequest request)
    {
        using var item = ShellItems.Parse(request.Target, false);
        if (item == null)
            return null;

        using var stream = item.BindToHandler<DirectN.IStream>(ShellN.Constants.BHID_Stream);
        if (stream == null)
            return null;

        using var decoder = WicImagingFactory.CreateDecoderFromStream(stream.Object);
        return FirstFrameOf(request, decoder);
    }

    private static ShellImage? FirstFrameOf(in ShellImageRequest request, IComObject<IWICBitmapDecoder> decoder)
    {
        if (decoder.GetFrameCount() == 0)
            return null;

        using var frame = decoder.GetFrame(0);
        var size = frame.GetSizeU();
        if (size.width == 0 || size.height == 0)
            return null;

        // never decoded larger than the picture really is, an enlarged image is just a blurred one.
        var fit = MathF.Min((float)request.Size / size.width, (float)request.Size / size.height);
        var width = (uint)MathF.Max(1, MathF.Round(size.width * MathF.Min(1, fit)));
        var height = (uint)MathF.Max(1, MathF.Round(size.height * MathF.Min(1, fit)));

        if (width == size.width && height == size.height)
            return ToImage(request, frame, null);

        using var scaler = WicImagingFactory.CreateBitmapScaler();
        scaler.Object.Initialize(frame.Object, width, height, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant).ThrowOnError();
        return ToImage(request, scaler, null);
    }

    // the shell will happily describe a file that does not exist, given a bind context carrying the attributes it would have had.
    // so an extension alone yields an icon, with no file touched and no disk access, and one call covers every file sharing that extension.
    private static async Task<ShellImage?> LoadExtensionIconAsync(ShellImageRequest request)
    {
        var attributes = request.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
        var name = Path.Join(_syntheticRoot, _syntheticName + request.Target);

        const SIIGBF flags = SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_RESIZETOFIT;
        var size = new SIZE(request.Size, request.Size);
        var typeName = GetTypeName(name, attributes);

        using (var context = IBindCtxExtensions.CreateBindCtx(name, attributes: attributes, throwOnError: false))
        using (var item = ShellItem.FromParsingName(name, context?.Object, throwOnError: false))
        {
            if (item != null)
            {
                using var bitmap = await item.GetImageAsBitmapAsync(size, flags, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
                if (bitmap != null)
                    return ToImage(request, bitmap, typeName);
            }
        }

        // some types register their icon as the file itself, so there is nothing to extract from a file that does not exist.
        if (request.SamplePath == null)
            return null;

        using var sample = ShellItems.Parse(request.SamplePath, false);
        if (sample == null)
            return null;

        using var sampleBitmap = await sample.GetImageAsBitmapAsync(size, flags, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
        return sampleBitmap == null ? null : ToImage(request, sampleBitmap, typeName);
    }

    private static unsafe string? GetTypeName(string name, FileAttributes attributes)
    {
        const SHGFI_FLAGS flags = SHGFI_FLAGS.SHGFI_TYPENAME | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES;

        var native = attributes.HasFlag(FileAttributes.Directory)
            ? FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY
            : FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL;

        var info = new SHFILEINFOW();
        if (ShellN.Functions.SHGetFileInfoW(PWSTR.From(name), native, (nint)(&info), (uint)sizeof(SHFILEINFOW), flags) == 0)
            return null;

        var typeName = info.szTypeName.ToString();
        return typeName.Length == 0 ? null : typeName;
    }

    private static async Task<ShellImage?> LoadShellImageAsync(ShellImageRequest request)
    {
        using var item = ShellItems.Parse(request.Target, request.IsDirectory);
        if (item == null)
            return null;

        var flags = SIIGBF.SIIGBF_RESIZETOFIT;
        if (request.CachedOnly)
        {
            flags |= SIIGBF.SIIGBF_INCACHEONLY;
        }

        if (request.Kind == ShellImageKind.FileIcon)
        {
            flags |= SIIGBF.SIIGBF_ICONONLY;
        }
        else
        {
            flags |= SIIGBF.SIIGBF_THUMBNAILONLY;
        }

        var size = new SIZE(request.Size, request.Size);

        // the asynchronous form is the one that copes with E_PENDING, which is what the shell returns while it is still building the image.
        // the synchronous one simply reports no image and the thumbnail may never appear.
        using var bitmap = await item.GetImageAsBitmapAsync(size, flags, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
        if (bitmap != null)
        {
            var image = ToImage(request, bitmap, null);

            // whatever the shell had cached can be any size at all, which is how one folder ends up showing thumbnails at a dozen different sizes.
            // it is displayed at once, then extracted properly.
            image.Provisional = request.CachedOnly && image.Width < request.Size && image.Height < request.Size;
            return image;
        }

        // plenty of files have no thumbnail at all, and their icon is the right answer rather than a blank cell.
        if (request.Kind != ShellImageKind.Thumbnail || request.CachedOnly)
            return null;

        const SIIGBF iconFlags = SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_ICONONLY;
        using var icon = await item.GetImageAsBitmapAsync(size, iconFlags, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
        return icon == null ? null : ToImage(request, icon, null);
    }

    private static ShellImage ToImage(in ShellImageRequest request, IComObject<IWICBitmapSource> bitmap, string? typeName)
    {
        var size = bitmap.GetSizeU();

        // the render target wants premultiplied BGRA, whatever the shell handed back.
        using var converter = WicImagingFactory.CreateFormatConverter();
        converter.Object.Initialize(
            bitmap.Object,
            DirectN.Constants.GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherType.WICBitmapDitherTypeNone,
            null!,
            0,
            WICBitmapPaletteType.WICBitmapPaletteTypeCustom).ThrowOnError();

        var stride = size.width * _bytesPerPixel;
        var pixels = new byte[stride * size.height];
        unsafe
        {
            fixed (byte* pointer = pixels)
            {
                converter.Object.CopyPixels(0, stride, (uint)pixels.Length, (nint)pointer).ThrowOnError();
            }
        }

        return new ShellImage
        {
            Key = request.Key,
            Width = size.width,
            Height = size.height,
            Pixels = pixels,
            Generation = request.Generation,
            TypeName = typeName,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _signal.Dispose();
    }
}
