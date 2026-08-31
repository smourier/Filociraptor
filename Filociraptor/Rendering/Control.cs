namespace Filociraptor.Rendering;

// what every drawn thing that takes input has in common.
internal abstract class Control
{
    public D2D_RECT_F Bounds { get; set; }

    public float Scale { get; protected set; } = 1;

    // whether it takes input at all just now. a menu that is not up takes none.
    public virtual bool IsInteractive => true;

    public virtual bool IsModal => false;

    // set while something inside is being dragged, so the window keeps the mouse until the button comes up.
    public virtual bool IsCapturing => false;

    public virtual bool Contains(float x, float y) => x >= Bounds.left && x < Bounds.right && y >= Bounds.top && y < Bounds.bottom;

    // each of these returns true when it dealt with the message.
    public virtual bool OnMouseMove(float x, float y) => false;
    public virtual bool OnMouseDown(float x, float y, bool doubleClick) => false;
    public virtual bool OnMouseUp() => false;
    public virtual bool OnWheel(float x, float y, int delta) => false;
    public virtual bool OnKeyDown(VIRTUAL_KEY key) => false;
}
