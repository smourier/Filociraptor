namespace Filociraptor.Rendering;

internal static class ImageDrawing
{
    // filling a square with the middle of the picture rather than fitting the whole of it inside one.
    // it is what makes a wall of thumbnails line up, at the price of the edges of anything not already square.
    public static unsafe void DrawSquare(
        IComObject<ID2D1DeviceContext> deviceContext,
        IComObject<ID2D1Bitmap> bitmap,
        float centreX,
        float centreY,
        float side,
        float opacity)
    {
        var size = bitmap.GetSize();
        if (size.width <= 0 || size.height <= 0)
            return;

        // the largest square the picture holds, taken from its middle.
        var source = MathF.Min(size.width, size.height);
        var sourceRect = new D2D_RECT_F
        {
            left = (size.width - source) / 2,
            top = (size.height - source) / 2,
            right = (size.width + source) / 2,
            bottom = (size.height + source) / 2,
        };

        // the square is filled, even when that means enlarging the crop a little.
        // the whole point of the option is that every cell is the same size, and a wide picture only has its short side to give,
        // so refusing to enlarge would leave a wall of different sized squares, which is what it was meant to avoid.
        var drawn = MathF.Round(side);
        var rect = new D2D_RECT_F
        {
            left = MathF.Round(centreX - drawn / 2),
            top = MathF.Round(centreY - drawn / 2),
        };

        rect.right = rect.left + drawn;
        rect.bottom = rect.top + drawn;

        var mode = drawn == source ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
        deviceContext.Object.DrawBitmap(bitmap.Object, (nint)(&rect), opacity, mode, (nint)(&sourceRect), 0);
    }

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

        // cubic whenever the size changes at all, in either direction.
        // at the original size there is nothing to interpolate, so the cheap mode does just as well.
        var mode = width == size.width ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
        deviceContext.Object.DrawBitmap(bitmap.Object, (nint)(&rect), opacity, mode, 0, 0);
    }
}
