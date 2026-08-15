namespace Filociraptor.Rendering;

internal static class ImageDrawing
{

    // icons and thumbnails are bitmaps, so they only stay sharp if they land on whole pixels and are never blown up past their own resolution.
    // an image smaller than the space given to it is drawn at its own size rather than stretched, which is the difference between a crisp icon and a blurry one.
    public static unsafe void Draw(
        IComObject<ID2D1DeviceContext> deviceContext,
        IComObject<ID2D1Bitmap> bitmap,
        float centreX,
        float centreY,
        float available,
        bool allowUpscale,
        float opacity)
    {
        var size = bitmap.GetSize();
        if (size.width <= 0 || size.height <= 0)
            return;

        var scale = MathF.Min(available / size.width, available / size.height);
        if (scale > 1 && !allowUpscale)
        {
            scale = 1;
        }

        var width = MathF.Round(size.width * scale);
        var height = MathF.Round(size.height * scale);
        var rect = new D2D_RECT_F
        {
            left = MathF.Round(centreX - width / 2),
            top = MathF.Round(centreY - height / 2),
        };

        rect.right = rect.left + width;
        rect.bottom = rect.top + height;

        // cubic whenever the size changes at all, in either direction. at the original size there is nothing to
        // interpolate, so the cheap mode does just as well.
        var mode = width == size.width
            ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR
            : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;

        deviceContext.Object.DrawBitmap(bitmap.Object, (nint)(&rect), opacity, mode, 0, 0);
    }
}
