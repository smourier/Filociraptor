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

            yield return await Task.Run(() => Describe(root), cancellationToken).ConfigureAwait(true);
        }
    }

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
