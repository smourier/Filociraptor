namespace Filociraptor.FileSystem;

// watches the folder on screen so the listing does not go stale.
// changes arrive in bursts, a copy or an install can fire thousands of events in a second, and each one would otherwise cost a full rescan.
// so events only restart a quiet timer, and the folder is read again once things have settled.
internal sealed class FolderWatcher : IDisposable
{
    private const int _quietMilliseconds = 400;

    private readonly Action _changed;
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private string? _watched;
    private bool _disposed;

    public FolderWatcher(Action changed)
    {
        _changed = changed;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Watch(string path)
    {
        // a refresh re-navigates to the same folder, and tearing the watcher down and building it again each time would drop whatever changed in the gap.
        if (string.Equals(_watched, path, StringComparison.OrdinalIgnoreCase) && _watcher != null)
            return;

        Stop();
        _watched = path;
        if (_disposed || string.IsNullOrEmpty(path))
            return;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                // we want only these.
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };

            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Renamed += OnRenamed;

            // too many changes at once and the buffer is lost, which is itself a reason to read the folder again.
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            Application.TraceWarning($"'{path}' could not be watched: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        _watched = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        var watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher == null)
            return;

        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Restart();
    private void OnRenamed(object sender, RenamedEventArgs e) => Restart();
    private void OnError(object sender, ErrorEventArgs e) => Restart();

    private void Restart()
    {
        if (_disposed)
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
