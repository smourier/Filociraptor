namespace Filociraptor.Rendering;

// a bigger look at the image under the pointer.
// it uses the same loader as the thumbnails, so it costs one more cached image and nothing at all on the UI thread.
internal sealed class ImagePreview
{
    // the preview takes this much of the window, so it is the same size whatever the picture is.
    private const float _windowFraction = 0.75f;
    private const float _padding = 8;
    private const float _radius = 8;

    public int Position { get; private set; } = -1;
    public bool Visible => Position >= 0;

    public void Show(int position) => Position = position;
    public bool Hide()
    {
        if (Position < 0)
            return false;

        Position = -1;
        return true;
    }

    public void Render(
        IComObject<ID2D1DeviceContext> deviceContext,
        RenderResources resources,
        ImageCache images,
        FolderItems items,
        string folderPath,
        in D2D_RECT_F client)
    {
        if (!Visible || Position >= items.Count)
            return;

        ref readonly var entry = ref items.EntryAt(Position);
        var padding = MathF.Round(_padding * resources.DpiScale);
        var boxWidth = (client.right - client.left) * _windowFraction - padding * 2;
        var boxHeight = (client.bottom - client.top) * _windowFraction - padding * 2;
        if (boxWidth <= 0 || boxHeight <= 0)
            return;

        var image = images.GetOrRequestPreview(items.NameOf(entry), folderPath, (int)MathF.Max(boxWidth, boxHeight));

        // nothing is drawn until the image is there, so the preview appears complete rather than as an empty frame.
        if (image == null)
            return;

        var size = image.GetSize();
        if (size.width <= 0 || size.height <= 0)
            return;

        // the picture keeps its proportions and fills the box, enlarged when it is smaller than the box, so a preview is the same size whatever it is showing.
        var fit = MathF.Min(boxWidth / size.width, boxHeight / size.height);
        var width = MathF.Round(size.width * fit);
        var height = MathF.Round(size.height * fit);
        var panelWidth = width + padding * 2;
        var panelHeight = height + padding * 2;

        // centred, so it does not move about as the pointer does.
        var left = MathF.Round((client.left + client.right - panelWidth) / 2);
        var top = MathF.Round((client.top + client.bottom - panelHeight) / 2);

        var panel = new D2D_RECT_F { left = left, top = top, right = left + panelWidth, bottom = top + panelHeight };
        var radius = _radius * resources.DpiScale;
        var rounded = new D2D1_ROUNDED_RECT { rect = panel, radiusX = radius, radiusY = radius };
        deviceContext.Object.FillRoundedRectangle(rounded, resources.OverlayBackgroundBrush.Object);
        deviceContext.Object.DrawRoundedRectangle(rounded, resources.LineBrush.Object, 1, null!);

        ImageDrawing.Draw(deviceContext, image, left + panelWidth / 2, top + panelHeight / 2, MathF.Max(width, height), true, 1);
    }
}
