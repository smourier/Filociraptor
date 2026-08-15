namespace Filociraptor.Rendering;

internal static class TextDrawing
{
    // draws straight from a span, so names come out of the character arena and numbers out of a stack buffer,
    // without a string ever being created.
    public static unsafe void Draw(
        IComObject<ID2D1DeviceContext> deviceContext,
        ReadOnlySpan<char> text,
        IComObject<IDWriteTextFormat> format,
        in D2D_RECT_F rect,
        IComObject<ID2D1Brush> brush)
    {
        if (text.Length == 0)
            return;

        fixed (char* pointer = text)
        {
            deviceContext.Object.DrawText(
                new PWSTR { Value = (nint)pointer },
                (uint)text.Length,
                format.Object,
                rect,
                brush.Object,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
        }
    }
}
