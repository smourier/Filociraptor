namespace Filociraptor.FileSystem;

// the listing of one folder, held as two flat buffers plus an index permutation.
// nothing here allocates per item, so opening a folder with a million files costs a handful of allocations in total.
internal sealed class FolderItems : IDisposable
{
    private const int _nameChunkShift = 16;   // 65536 chars, 128 KB per chunk.
    private const int _entryChunkShift = 13;  // 8192 entries, 256 KB per chunk.
    private const ulong _payloadMask = (1UL << 63) - 1;
    private const int _keyChars = 8;
    private const int _keyBits = 7;
    private const int _refineThreshold = 16;

    // a path component tops out at 255 characters, so this many rounds always outruns the longest shared prefix
    // and the comparer below is only ever reached for runs that are already tiny.
    private const int _maxRefineDepth = 32;

    private readonly ChunkedBuffer<char> _names = new(_nameChunkShift);
    private readonly ChunkedBuffer<FileEntry> _entries = new(_entryChunkShift);
    private readonly NameComparer _ascending;
    private readonly NameComparer _descending;
    private int _publishedCount;
    private int[]? _order;
    private ulong[]? _keys;
    private int _sortedCount;

    public FolderItems()
    {
        _ascending = new NameComparer(this, false);
        _descending = new NameComparer(this, true);
    }

    public int Count => Volatile.Read(ref _publishedCount);
    public double KeyMilliseconds { get; private set; }
    public double PrimarySortMilliseconds { get; private set; }
    public double RefineMilliseconds { get; private set; }
    public int ComparerCalls { get; private set; }
    public long BufferBytes => _names.CapacityBytes + _entries.CapacityBytes;
    public SortColumn SortColumn { get; private set; }
    public bool SortDescending { get; private set; }

    public void Reset()
    {
        Volatile.Write(ref _publishedCount, 0);
        _names.Clear();
        _entries.Clear();
        _order = null;
        _sortedCount = 0;
    }

    // called on the scan thread only.
    public void Add(ref FileSystemEntry entry)
    {
        var name = entry.FileName;
        var isDirectory = entry.IsDirectory;
        var item = new FileEntry
        {
            NameOffset = _names.AddRange(name),
            NameLength = name.Length,
            ExtensionOffset = ExtensionOffsetOf(name),
            Attributes = entry.Attributes,
            Size = isDirectory ? 0 : entry.Length,
            LastWriteTicks = entry.LastWriteTimeUtc.UtcTicks,
        };
        _entries.Add(item);
    }

    // makes everything appended so far visible to the render thread.
    public void Publish() => Volatile.Write(ref _publishedCount, _entries.Count);

    public int IndexAt(int position) => _order != null && position < _sortedCount ? _order[position] : position;
    public ref readonly FileEntry EntryAt(int position) => ref _entries[IndexAt(position)];
    public ReadOnlySpan<char> NameOf(in FileEntry entry) => _names.GetSpan(entry.NameOffset, entry.NameLength);

    public ReadOnlySpan<char> ExtensionOf(in FileEntry entry)
    {
        if (entry.ExtensionOffset < 0)
            return default;

        var skip = entry.ExtensionOffset + 1;
        return _names.GetSpan(entry.NameOffset + skip, entry.NameLength - skip);
    }

    public void Sort(SortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;

        var count = Count;
        if (count == 0)
        {
            _sortedCount = 0;
            return;
        }

        if (_order == null || _order.Length < count)
        {
            _order = new int[count];
            _keys = new ulong[count];
        }

        ComparerCalls = 0;
        var order = _order;
        var keys = _keys!;

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            keys[i] = KeyOf(i, column, descending);
        }

        var keysDone = Stopwatch.GetTimestamp();
        Array.Sort(keys, order, 0, count);

        var primaryDone = Stopwatch.GetTimestamp();

        // the packed key only holds a prefix, so runs that share one are still unordered.
        RefineRuns(keys, order, count, descending, column);
        var refineDone = Stopwatch.GetTimestamp();

        var scale = 1000d / Stopwatch.Frequency;
        KeyMilliseconds = (keysDone - start) * scale;
        PrimarySortMilliseconds = (primaryDone - keysDone) * scale;
        RefineMilliseconds = (refineDone - primaryDone) * scale;
        _sortedCount = count;
    }

    private void RefineRuns(ulong[] keys, int[] order, int count, bool descending, SortColumn column)
    {
        // a name that already contributed its first characters to the primary key resumes past them.
        var offset = column == SortColumn.Name ? _keyChars : 0;
        var start = 0;
        while (start < count)
        {
            var end = start + 1;
            while (end < count && keys[end] == keys[start])
            {
                end++;
            }

            if (end - start > 1)
            {
                SortByName(keys, order, start, end - start, descending, offset, 0);
            }

            start = end;
        }
    }

    // folders like WinSxS hold tens of thousands of names sharing a long prefix, which makes a single packed key
    // useless and a comparer based sort the only thing left. so each tie is re sorted on the next characters instead,
    // which keeps the whole sort on the integer path until the runs are genuinely small.
    private void SortByName(ulong[] keys, int[] order, int start, int length, bool descending, int charOffset, int depth)
    {
        if (length <= 1)
            return;

        if (depth >= _maxRefineDepth || length <= _refineThreshold)
        {
            Array.Sort(order, start, length, descending ? _descending : _ascending);
            return;
        }

        charOffset += CommonPrefixOf(order, start, length, charOffset);

        for (var i = start; i < start + length; i++)
        {
            var name = NameOf(_entries[order[i]]);
            var key = PrefixKey(charOffset < name.Length ? name[charOffset..] : default);
            keys[i] = descending ? ~key : key;
        }

        Array.Sort(keys, order, start, length);

        var end = start + length;
        var runStart = start;
        while (runStart < end)
        {
            var runEnd = runStart + 1;
            while (runEnd < end && keys[runEnd] == keys[runStart])
            {
                runEnd++;
            }

            SortByName(keys, order, runStart, runEnd - runStart, descending, charOffset + _keyChars, depth + 1);
            runStart = runEnd;
        }
    }

    private ulong KeyOf(int index, SortColumn column, bool descending)
    {
        ref var entry = ref _entries[index];
        var payload = column switch
        {
            SortColumn.Size => (ulong)entry.Size,
            SortColumn.Modified => (ulong)entry.LastWriteTicks,
            SortColumn.Type => PrefixKey(ExtensionOf(entry)),
            _ => PrefixKey(NameOf(entry)),
        };

        if (descending)
        {
            payload = ~payload;
        }

        // folders always come first, in both directions, the way Explorer does it.
        var folderBit = entry.IsDirectory ? 0UL : 1UL;
        return (folderBit << 63) | (payload & _payloadMask);
    }

    // number of characters every name in the run already shares, found with a vectorised scan.
    // this is an ordinal match, so it can only ever be shorter than the case insensitive one, which means skipping
    // it can never step over a character that would have decided the order.
    private int CommonPrefixOf(int[] order, int start, int length, int charOffset)
    {
        var first = NameOf(_entries[order[start]]);
        if (charOffset >= first.Length)
            return 0;

        var head = first[charOffset..];
        var common = head.Length;
        for (var i = start + 1; i < start + length; i++)
        {
            var other = NameOf(_entries[order[i]]);
            if (charOffset >= other.Length)
                return 0;

            var candidate = head.CommonPrefixLength(other[charOffset..]);
            if (candidate < common)
            {
                common = candidate;
                if (common == 0)
                    return 0;
            }
        }

        return common;
    }

    // packs the first characters into one integer so the bulk of the sort is a plain integer sort.
    private static ulong PrefixKey(ReadOnlySpan<char> text)
    {
        ulong key = 0;
        for (var i = 0; i < _keyChars; i++)
        {
            var value = i < text.Length ? Fold(text[i]) : 0UL;
            key |= value << ((_keyChars - 1 - i) * _keyBits);
        }
        return key;
    }

    // case insensitive fold into 7 bits. anything outside ASCII lands on the same value and gets ordered by the tie break.
    private static ulong Fold(char c)
    {
        if (c >= 'a' && c <= 'z')
            return (ulong)(c - ('a' - 'A'));

        return c < 128 ? c : 127UL;
    }

    private static int ExtensionOffsetOf(ReadOnlySpan<char> name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? -1 : dot;
    }

    public void Dispose()
    {
        _names.Dispose();
        _entries.Dispose();
    }

    private sealed class NameComparer(FolderItems items, bool descending) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            items.ComparerCalls++;
            var left = items.NameOf(items._entries[x]);
            var right = items.NameOf(items._entries[y]);
            var result = left.CompareTo(right, StringComparison.OrdinalIgnoreCase);
            return descending ? -result : result;
        }
    }
}
