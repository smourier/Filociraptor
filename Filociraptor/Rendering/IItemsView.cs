namespace Filociraptor.Rendering;

// what the window needs from whichever view is showing the listing.
internal interface IItemsView
{
    FolderItems? Items { get; set; }
    D2D_RECT_F Bounds { get; set; }
    Action<int>? ItemActivated { get; set; }

    // both views share one of these, so the selection survives a change of view.
    Selection Selection { get; set; }

    // how far a selection moves for one row of travel, which is a whole row of cells in a grid.
    int Columns { get; }
    int PageSize { get; }

    void Reset();
    float ScrollOffset { get; set; }

    int HoverPosition { get; }

    int PositionAtPoint(float x, float y);
    void EnsureVisible(int position);

    // what a click or a key does to the selection is written once here rather than once per view,
    // because it is the same everywhere and the views differ only in where a position sits on screen.
    void Select(int position)
    {
        var count = Items?.Count ?? 0;
        if (count == 0)
            return;

        var clamped = Math.Clamp(position, 0, count - 1);
        Selection.Set(clamped);
        EnsureVisible(clamped);
    }

    // with shift it is everything from the anchor, with control it is this one added or taken away, and with neither it is this one alone.
    void SelectAt(int position)
    {
        var count = Items?.Count ?? 0;
        if (count == 0 || position < 0)
            return;

        if (Keyboard.IsShiftDown)
        {
            Selection.ExtendTo(position, count);
        }
        else if (Keyboard.IsControlDown)
        {
            Selection.Toggle(position);
        }
        else if (Selection.Count > 1 && Selection.Contains(position))
        {
            Selection.Defer(position);
        }
        else
        {
            Selection.Set(position);
        }

        EnsureVisible(position);
    }

    void MoveSelection(int delta)
    {
        var count = Items?.Count ?? 0;
        if (count == 0)
            return;

        var from = Selection.Current < 0 ? 0 : Selection.Current;
        SelectAt(Math.Clamp(from + delta, 0, count - 1));
    }

    void SelectAll() => Selection.SetAll(Items?.Count ?? 0);
    void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, ImageCache images, string folderPath, bool streamItems);
}
