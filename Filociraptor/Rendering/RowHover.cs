namespace Filociraptor.Rendering;

// the hover of a list, where the row being left fades out while the row being entered fades in.
internal struct RowHover
{
    private HoverAnimation _entering;
    private HoverAnimation _leaving;
    private int _index;
    private int _leavingIndex;

    public RowHover()
    {
        _index = -1;
        _leavingIndex = -1;
    }

    public readonly int Index => _index;

    // true when the row under the pointer changed, which is what asks for a frame.
    public bool MoveTo(int index)
    {
        if (index == _index)
            return false;

        // the row being left carries on from wherever its fade had reached, so running down a list cross fades rather than restarting from nothing.
        _leavingIndex = _index;
        _leaving = _entering;
        _index = index;
        _entering = default;
        return true;
    }

    // true while either row is still travelling.
    public bool Advance(float elapsedSeconds)
    {
        var moving = _entering.Advance(_index >= 0, elapsedSeconds);
        return _leaving.Advance(false, elapsedSeconds) || moving;
    }

    public readonly float OpacityOf(int index) => index == _index ? _entering.Opacity : index == _leavingIndex ? _leaving.Opacity : 0;

    public void Reset()
    {
        _index = -1;
        _leavingIndex = -1;
        _entering = default;
        _leaving = default;
    }
}
