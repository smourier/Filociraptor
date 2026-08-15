namespace Filociraptor.Rendering;

// what the window needs from whichever view is showing the listing.
internal interface IItemsView
{
    FolderItems? Items { get; set; }
    D2D_RECT_F Bounds { get; set; }
    Action<int>? ItemActivated { get; set; }
    int SelectedPosition { get; }

    // how far a selection moves for one row of travel, which is a whole row of cells in a grid.
    int Columns { get; }
    int PageSize { get; }

    bool ScrollbarDragging { get; }

    void Reset();
    void ScrollByWheel(int wheelDelta);
    bool BeginScrollbarDrag(float x, float y);
    void DragScrollbar(float y);
    void EndScrollbarDrag();
    bool SetHover(float x, float y);
    bool OnClick(float x, float y, bool doubleClick);
    float ScrollOffset { get; set; }

    int HoverPosition { get; }

    int PositionAtPoint(float x, float y);
    void Select(int position);
    void MoveSelection(int delta);
    void Render(IComObject<ID2D1DeviceContext> deviceContext, RenderResources resources, ImageCache images, string folderPath);
}
