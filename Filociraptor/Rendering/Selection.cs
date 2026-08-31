namespace Filociraptor.Rendering;

internal sealed class Selection
{
    private readonly HashSet<int> _positions = [];

    public int Current { get; private set; } = -1;

    public int Anchor { get; private set; } = -1;

    public int Deferred { get; private set; } = -1;

    public int Count => _positions.Count;

    public bool Contains(int position) => _positions.Contains(position);

    public IEnumerable<int> Positions => _positions.Order();

    public void Defer(int position) => Deferred = position;

    public bool ApplyDeferred()
    {
        if (Deferred < 0)
            return false;

        Set(Deferred);
        return true;
    }

    public void Clear()
    {
        Deferred = -1;
        _positions.Clear();
        Current = -1;
        Anchor = -1;
    }

    // a plain click, or an arrow key. everything else goes.
    public void Set(int position)
    {
        Deferred = -1;
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

    // CTRL, which adds one or takes one away and moves the anchor there either way.
    public void Toggle(int position)
    {
        Deferred = -1;
        if (position < 0)
            return;

        if (!_positions.Remove(position))
        {
            _positions.Add(position);
        }

        Current = position;
        Anchor = position;
    }

    // SHIFT, which is everything from the anchor to here and nothing else.
    public void ExtendTo(int position, int count)
    {
        Deferred = -1;
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
        Deferred = -1;
        _positions.Clear();
        for (var i = 0; i < count; i++)
        {
            _positions.Add(i);
        }

        Current = count > 0 ? 0 : -1;
        Anchor = Current;
    }
}
