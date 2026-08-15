namespace Filociraptor.Rendering;

// formats into a caller supplied stack buffer.
// sizes and dates are formatted for every visible row on every frame, so going through string would allocate
// thousands of times a second for text that is thrown away immediately.
internal ref struct ScratchText(Span<char> buffer)
{
    private readonly Span<char> _buffer = buffer;
    private int _length;

    public readonly ReadOnlySpan<char> Text => _buffer[.._length];

    public void Clear() => _length = 0;

    public void Append(ReadOnlySpan<char> value)
    {
        if (value.Length > _buffer.Length - _length)
            return;

        value.CopyTo(_buffer[_length..]);
        _length += value.Length;
    }

    public void Append(char value)
    {
        if (_length >= _buffer.Length)
            return;

        _buffer[_length++] = value;
    }

    public void Append(long value)
    {
        if (value.TryFormat(_buffer[_length..], out var written))
        {
            _length += written;
        }
    }

    public void Append(double value, ReadOnlySpan<char> format)
    {
        if (value.TryFormat(_buffer[_length..], out var written, format))
        {
            _length += written;
        }
    }

    public void AppendDateTime(DateTime value)
    {
        if (value.TryFormat(_buffer[_length..], out var written, "yyyy-MM-dd HH:mm"))
        {
            _length += written;
        }
    }

    public void AppendSize(long bytes)
    {
        const long kilo = 1024;
        const long mega = kilo * 1024;
        const long giga = mega * 1024;
        const long tera = giga * 1024;

        if (bytes < kilo)
        {
            Append(bytes);
            Append(" B");
            return;
        }

        double value;
        ReadOnlySpan<char> unit;
        if (bytes < mega)
        {
            value = (double)bytes / kilo;
            unit = " KB";
        }
        else if (bytes < giga)
        {
            value = (double)bytes / mega;
            unit = " MB";
        }
        else if (bytes < tera)
        {
            value = (double)bytes / giga;
            unit = " GB";
        }
        else
        {
            value = (double)bytes / tera;
            unit = " TB";
        }

        Append(value, value >= 100 ? "F0" : "F1");
        Append(unit);
    }
}
