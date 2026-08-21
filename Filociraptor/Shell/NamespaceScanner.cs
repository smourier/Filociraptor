using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// reads a folder that has no path, This PC, the recycle bin, a device.
// every item costs a COM object, a string and calls, which is why this is not used for folders on disk (perf is 5x worse).
// those places hold a few items rather than thousands, so the cost never shows.
internal static class NamespaceScanner
{
    private const int _publishEvery = 64;

    public static async IAsyncEnumerable<int> ScanAsync(ShellLocation location, FileSystem.FolderItems items, bool showHidden, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var producer = Task.Run(() => Produce(location, items, showHidden, channel.Writer, cancellationToken), cancellationToken);

        await foreach (var count in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(true))
        {
            yield return count;
        }

        await producer.ConfigureAwait(true);
    }

    private static void Produce(ShellLocation location, FileSystem.FolderItems items, bool showHidden, ChannelWriter<int> writer, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            using var folder = location.Bind() as ShellFolder;
            if (folder == null)
            {
                Application.TraceWarning($"'{location.ParsingName}' could not be opened as a folder.");
            }
            else
            {
                var flags = _SHCONTF.SHCONTF_FOLDERS | _SHCONTF.SHCONTF_NONFOLDERS;
                if (showHidden)
                {
                    flags |= _SHCONTF.SHCONTF_INCLUDEHIDDEN | _SHCONTF.SHCONTF_INCLUDESUPERHIDDEN;
                }

                var pending = 0;
                foreach (var child in folder.EnumerateChildren(flags))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        child.Dispose();
                        break;
                    }

                    Add(items, child);
                    child.Dispose();

                    pending++;
                    if (pending >= _publishEvery)
                    {
                        pending = 0;
                        items.Publish();
                        writer.TryWrite(items.Count);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
            Application.TraceError($"'{location.ParsingName}' could not be enumerated: {ex}");
        }

        items.Publish();
        writer.TryWrite(items.Count);
        writer.TryComplete(error);
    }

    private static unsafe void Add(FileSystem.FolderItems items, ShellItem child)
    {
        var name = child.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false);
        if (string.IsNullOrEmpty(name))
            return;

        var parsing = child.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false) ?? name;
        var attributes = child.Attributes;

        var flags = FileAttributes.None;
        if (attributes.HasFlag(SFGAO_FLAGS.SFGAO_FOLDER))
        {
            flags |= FileAttributes.Directory;
        }

        if (attributes.HasFlag(SFGAO_FLAGS.SFGAO_HIDDEN))
        {
            flags |= FileAttributes.Hidden;
        }

        // a parsing name is not always enough to find the item again.
        // for example, a portable device names its storage SID-{10003,Internal Storage,...} and then refuses to parse that back (?),
        // and the name is scoped to the connection anyway, so the id list the enumeration just handed over is copied into the listing with it.
        using var idList = child.GetIdList(false);
        var bytes = idList is null ? default : new ReadOnlySpan<byte>((void*)idList.Pointer, (int)idList.Size);
        items.AddNamespaceItem(name, parsing, flags, bytes);
    }
}
