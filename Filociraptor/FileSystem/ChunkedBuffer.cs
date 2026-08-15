namespace Filociraptor.FileSystem;

// unmanaged storage split into fixed size chunks.
// chunks are never reallocated, so a pointer handed out stays valid while a background thread keeps appending,
// which is what lets the render thread read entries that were published before it started drawing.
internal sealed unsafe class ChunkedBuffer<T> : IDisposable where T : unmanaged
{
    private const int _maxChunks = 8192;

    private readonly nint[] _chunks = new nint[_maxChunks];
    private readonly int _chunkShift;
    private readonly int _chunkSize;
    private readonly int _chunkMask;
    private int _chunkCount;
    private int _count;
    private bool _disposed;

    public ChunkedBuffer(int chunkShift)
    {
        _chunkShift = chunkShift;
        _chunkSize = 1 << chunkShift;
        _chunkMask = _chunkSize - 1;
    }

    public int Count => _count;
    public long CapacityBytes => (long)_chunkCount * _chunkSize * sizeof(T);

    public ref T this[int index]
    {
        get
        {
            var chunk = (T*)Volatile.Read(ref _chunks[index >> _chunkShift]);
            return ref chunk[index & _chunkMask];
        }
    }

    public T* GetPointer(int index)
    {
        var chunk = (T*)Volatile.Read(ref _chunks[index >> _chunkShift]);
        return chunk + (index & _chunkMask);
    }

    public ReadOnlySpan<T> GetSpan(int index, int length) => new(GetPointer(index), length);

    public int Add(in T value)
    {
        var index = _count;
        EnsureRoom(index, 1);
        this[index] = value;
        _count = index + 1;
        return index;
    }

    // appends without ever straddling a chunk boundary, so the result can be read back as a single span.
    public int AddRange(ReadOnlySpan<T> values)
    {
        if (values.Length > _chunkSize)
            throw new ArgumentOutOfRangeException(nameof(values));

        var index = _count;
        var offset = index & _chunkMask;
        if (offset + values.Length > _chunkSize)
        {
            // skip the tail of the current chunk.
            index += _chunkSize - offset;
        }

        EnsureRoom(index, values.Length);
        values.CopyTo(new Span<T>(GetPointer(index), values.Length));
        _count = index + values.Length;
        return index;
    }

    private void EnsureRoom(int index, int length)
    {
        var lastChunk = (index + length - 1) >> _chunkShift;
        while (_chunkCount <= lastChunk)
        {
            if (_chunkCount >= _maxChunks)
                throw new OutOfMemoryException();

            var chunk = (nint)NativeMemory.Alloc((nuint)_chunkSize * (nuint)sizeof(T));

            // publish the pointer before the count, the reader only trusts chunks below the count.
            Volatile.Write(ref _chunks[_chunkCount], chunk);
            Volatile.Write(ref _chunkCount, _chunkCount + 1);
        }
    }

    public void Clear() => _count = 0;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var i = 0; i < _chunkCount; i++)
        {
            NativeMemory.Free((void*)_chunks[i]);
            _chunks[i] = 0;
        }
        _chunkCount = 0;
    }
}
