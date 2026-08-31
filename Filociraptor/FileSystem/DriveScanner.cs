namespace Filociraptor.FileSystem;

// reading a volume label or a free space figure goes to the device, and on an empty card reader, a disconnected network drive or a spun down disk that can block for seconds.
// so the whole thing happens off the UI thread, and each drive is reported as soon as it answers rather than waiting for the slowest one.
internal static class DriveScanner
{
    public static async IAsyncEnumerable<DriveEntry> ScanAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var roots = await Task.Run(GetRoots, cancellationToken).ConfigureAwait(true);
        foreach (var root in roots)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return Name(root);
        }

        // how much room each one has is the part that costs, and every drive is asked at once and reported the moment it answers.
        var pending = new List<Task<DriveEntry>>();
        foreach (var root in roots)
        {
            pending.Add(Task.Run(() => Describe(root), cancellationToken));
        }

        while (pending.Count > 0)
        {
            var answered = await Task.WhenAny(pending).ConfigureAwait(true);
            pending.Remove(answered);
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (answered.IsCompletedSuccessfully)
            {
                yield return answered.Result;
            }
        }
    }

    // the name and the kind of a drive are read from the mapping rather than from the device, so neither spins anything up nor waits for a network.
    private static DriveEntry Name(string root) => new()
    {
        Root = root,
        Label = string.Empty,
        Type = TypeOf(root),
        IsPending = true,
    };

    private static DriveType TypeOf(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch
        {
            return DriveType.Unknown;
        }
    }

    // the file system is asked, not the shell.
    // the shell's answer is the better one, it leaves out a letter with no device behind it, which is how a virtual machine's floppy controller turns into an A: that Explorer does not show.
    // but measured here it costs more.
    private static string[] GetRoots()
    {
        try
        {
            return [.. DriveInfo.GetDrives().Select(d => d.Name)];
        }
        catch (Exception ex)
        {
            Application.TraceError($"the drives could not be listed: {ex}");
            return [];
        }
    }

    private static DriveEntry Describe(string root)
    {
        var label = string.Empty;
        var type = DriveType.Unknown;
        var ready = false;
        long total = 0;
        long free = 0;

        try
        {
            var info = new DriveInfo(root);
            type = info.DriveType;
            ready = info.IsReady;
            if (ready)
            {
                label = info.VolumeLabel;
                total = info.TotalSize;
                free = info.AvailableFreeSpace;
            }
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"'{root}' could not be described: {ex.Message}");
        }

        return new DriveEntry
        {
            Root = root,
            Label = label,
            Type = type,
            IsReady = ready,
            TotalBytes = total,
            FreeBytes = free,
        };
    }
}
