namespace Filociraptor.Rendering;

// a hover that fades instead of snapping.
// it is driven by the time the last frame took rather than by a timer, so it costs nothing while nothing moves, and the window keeps drawing only for as long as something is still travelling.
internal struct HoverAnimation
{
    private const float _fadeInSeconds = 0.12f;
    private const float _fadeOutSeconds = 0.20f;

    private float _value;

    public readonly float Opacity => _value * _value * (3 - 2 * _value);

    public void Reset(float value) => _value = value;

    // true while it is still moving, which is what asks for another frame.
    public bool Advance(bool hot, float elapsedSeconds)
    {
        var target = hot ? 1f : 0f;
        if (_value == target)
            return false;

        // leaving is slower than arriving, which is what makes a row of buttons feel settled rather than twitchy.
        var step = elapsedSeconds / (hot ? _fadeInSeconds : _fadeOutSeconds);
        _value = hot ? MathF.Min(target, _value + step) : MathF.Max(target, _value - step);
        return _value != target;
    }
}
