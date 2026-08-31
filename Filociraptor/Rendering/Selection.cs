namespace Filociraptor.Rendering;

// what is selected in the listing, and the rules for changing it.
// the two views share one, so a change of view keeps the selection.
internal sealed class Selection
{
    private readonly HashSet<int> _positions = [];

    // the one a plain click landed on. it is what gets opened, and what the arrows move.
    public int Current { get; private set; } = -1;

    // where a range starts, which is the last position chosen without a shift.
    public int Anchor { get; private set; } = -1;

    public int Count => _positions.Count;
    public bool Contains(int position) => _positions.Contains(position);

    // in the order they appear, because that is the order anything asked about them expects.
    public IEnumerable<int> Positions => _positions.Order();

    public void Clear()
    {
        _positions.Clear();
        Current = -1;
        Anchor = -1;
    }

    // a plain click, or an arrow key. everything else goes.
    public void Set(int position)
    {
        _positions.Clear();
        if (position < 0)
        {
            Current = -1;
            Anchor = -1;
            return;
        }

        _positions.Add(position);
        Current = position;
        Anchor = position;
    }

    // control, which adds one or takes one away and moves the anchor there either way.
    public void Toggle(int position)
    {
        if (position < 0)
            return;

        if (!_positions.Remove(position))
        {
            _positions.Add(position);
        }

        Current = position;
        Anchor = position;
    }

    // shift, which is everything from the anchor to here and nothing else.
    // the anchor stays where it was, so shifting again replaces the range rather than growing it.
    public void ExtendTo(int position, int count)
    {
        if (position < 0 || count <= 0)
            return;

        var anchor = Anchor < 0 ? position : Anchor;
        var first = Math.Min(anchor, position);
        var last = Math.Max(anchor, position);

        _positions.Clear();
        for (var i = first; i <= last; i++)
        {
            if (i >= 0 && i < count)
            {
                _positions.Add(i);
            }
        }

        Current = position;
        Anchor = anchor;
    }

    public void SetAll(int count)
    {
        _positions.Clear();
        for (var i = 0; i < count; i++)
        {
            _positions.Add(i);
        }

        Current = count > 0 ? 0 : -1;
        Anchor = Current;
    }
}
