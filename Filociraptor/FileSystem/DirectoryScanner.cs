namespace Filociraptor.FileSystem;

// there is no asynchronous directory enumeration in Windows, FindFirstFileEx is synchronous.
// so the scan runs on a worker and streams batches back, which keeps the UI thread free,
// makes the first rows appear after a few milliseconds whatever the folder size,
// and gives navigation a cancellation point it can hit instantly.
internal static class DirectoryScanner
{
    private const int _publishEvery = 4096;

    public static async IAsyncEnumerable<int> ScanAsync(string path, FolderItems items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var producer = Task.Run(() => Produce(path, items, channel.Writer, cancellationToken), cancellationToken);

        await foreach (var count in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(true))
        {
            yield return count;
        }

        await producer.ConfigureAwait(true);
    }

    // the synchronous core, also used directly when there is no UI to keep responsive.
    public static void Scan(string path, FolderItems items, Action<int>? batchPublished, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        // the transform copies straight into the flat buffers. FileSystemEntry is a ref struct over the raw
        // find data, so nothing is allocated per file, not even the name.
        var enumerable = new FileSystemEnumerable<byte>(path, (ref FileSystemEntry entry) =>
        {
            items.Add(ref entry);
            return 0;
        }, options);

        var pending = 0;
        foreach (var _ in enumerable)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            pending++;
            if (pending >= _publishEvery)
            {
                pending = 0;
                items.Publish();
                batchPublished?.Invoke(items.Count);
            }
        }

        items.Publish();
        batchPublished?.Invoke(items.Count);
    }

    private static void Produce(string path, FolderItems items, ChannelWriter<int> writer, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            Scan(path, items, count => writer.TryWrite(count), cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        writer.TryComplete(error);
    }
}
