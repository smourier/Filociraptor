using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// keeps a namespace folder current, the way FolderWatcher does for a folder on disk.
// there is no file system underneath virtual folders, so the shell is the only thing that can say when they change.
internal sealed class NamespaceWatcher : IDisposable
{
    // debouncing the events is necessary because a single change can generate many notifications
    private const int _quietMilliseconds = 400;

    // we're only interested in changes to the folder itself
    private const SHCNE_ID _globalNoise = SHCNE_ID.SHCNE_ASSOCCHANGED | SHCNE_ID.SHCNE_UPDATEIMAGE | SHCNE_ID.SHCNE_FREESPACE | SHCNE_ID.SHCNE_SERVERDISCONNECT;

    private readonly Action _changed;
    private readonly Timer _timer;
    private ChangeNotifier? _notifier;
    private ItemIdList? _idList;
    private string? _watched;
    private bool _disposed;

    public NamespaceWatcher(Action changed)
    {
        _changed = changed;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Watch(ShellLocation location)
    {
        var parsingName = location.ParsingName;
        if (string.Equals(_watched, parsingName, StringComparison.OrdinalIgnoreCase) && _notifier != null)
            return;

        Stop();
        if (_disposed || string.IsNullOrEmpty(parsingName))
            return;

        try
        {
            using var item = location.Bind();
            var idList = item?.GetIdList(false);
            if (idList is null)
            {
                Application.TraceWarning($"'{parsingName}' has no id list, so its changes cannot be listened to.");
                return;
            }

            var notifier = new ChangeNotifier();
            notifier.Notified += OnNotified;

            _idList = idList;
            _notifier = notifier;
            _watched = parsingName;
            _ = notifier.Run(idList, false, SHCNE_ID.SHCNE_ALLEVENTS);
        }
        catch (Exception ex)
        {
            Application.TraceError($"'{parsingName}' could not be listened to: {ex}");
            Stop();
        }
    }

    public void Stop()
    {
        _watched = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);

        var notifier = Interlocked.Exchange(ref _notifier, null);
        if (notifier != null)
        {
            notifier.Notified -= OnNotified;
            notifier.Dispose();
        }

        var idList = _idList;
        _idList = null;
        idList?.Dispose();
    }

    private void OnNotified(object? sender, ChangeNotifyEventArgs e)
    {
        if (_disposed)
            return;

        if (e.Event.HasValue && (e.Event.Value & _globalNoise) != 0)
            return;

        _timer.Change(_quietMilliseconds, Timeout.Infinite);
    }

    private void Fire()
    {
        if (_disposed)
            return;

        _changed();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _timer.Dispose();
    }
}
