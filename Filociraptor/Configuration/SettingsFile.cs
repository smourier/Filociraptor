namespace Filociraptor.Configuration;

// a file sitting next to the executable wins, which is what makes a copied folder portable, and it is only ever used when it is already there, so an ordinary install writes under the user's local app data instead.
internal static class SettingsFile
{
    private const string _fileName = "filo.settings.json";
    private const string _folderName = "Filociraptor";
    private const int _maxRecentFolders = 20;

    // one for the file, held while it is being written, and one for the queue of writes waiting to happen.
    // they are separate because the second is taken on the thread that draws, and that one must never wait on a disk.
    private static readonly Lock _lock = new();
    private static readonly Lock _queueLock = new();
    private static Task _writes = Task.CompletedTask;

    static SettingsFile()
    {
        var beside = BesidePath();
        if (beside != null && File.Exists(beside))
        {
            Location = beside;
            IsPortable = true;
            return;
        }

        Location = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _folderName,
            _fileName);
    }

    public static string Location { get; }
    public static bool IsPortable { get; }

    private static string? BesidePath()
    {
        var executable = Environment.ProcessPath;
        var folder = executable == null ? null : Path.GetDirectoryName(executable);
        return folder == null ? null : Path.Join(folder, _fileName);
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Location))
            {
                using var stream = File.OpenRead(Location);
                var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.Settings);
                if (loaded != null)
                {
                    loaded.RecentFolders.Sort(ByMostRecent);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // a settings file that cannot be read is not a reason to refuse to start, the defaults are all sound.
            Application.TraceError($"'{Location}' could not be read: {ex}");
        }

        return new Settings();
    }

    // on the way out, where there is no later.
    public static void Save(Settings settings) => Write(Snapshot(settings));

    // the settings are turned into bytes on the thread that owns them, which costs microseconds, and only the writing is handed away,
    // because a file written on the thread that draws is a frame that waits for a disk.
    public static void SaveLater(Settings settings)
    {
        var bytes = Snapshot(settings);
        if (bytes == null)
            return;

        // chained rather than thrown at the pool, so the last snapshot taken is the last one left on disk.
        lock (_queueLock)
        {
            _writes = _writes.ContinueWith(_ => Write(bytes), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static byte[]? Snapshot(Settings settings)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.Settings);
        }
        catch (Exception ex)
        {
            Application.TraceError($"the settings could not be turned into a file: {ex}");
            return null;
        }
    }

    private static void Write(byte[]? bytes)
    {
        if (bytes == null)
            return;

        try
        {
            lock (_lock)
            {
                var folder = Path.GetDirectoryName(Location);
                if (folder != null)
                {
                    Directory.CreateDirectory(folder);
                }

                var temporary = Location + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, Location, true);
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"'{Location}' could not be written: {ex}");
        }
    }

    private static int ByMostRecent(RecentFolder left, RecentFolder right) => right.LastVisited.CompareTo(left.LastVisited);

    public static void RememberFolder(Settings settings, string parsingName, string displayName)
    {
        if (string.IsNullOrEmpty(parsingName))
            return;

        settings.RecentFolders.RemoveAll(f => f.ParsingName.EqualsIgnoreCase(parsingName));
        settings.RecentFolders.Insert(0, new RecentFolder
        {
            ParsingName = parsingName,
            DisplayName = displayName,
            LastVisited = DateTime.Now,
        });

        if (settings.RecentFolders.Count > _maxRecentFolders)
        {
            settings.RecentFolders.RemoveRange(_maxRecentFolders, settings.RecentFolders.Count - _maxRecentFolders);
        }
    }

    public static bool ForgetAllFolders(Settings settings)
    {
        if (settings.RecentFolders.Count == 0)
            return false;

        settings.RecentFolders.Clear();
        return true;
    }

    // whether a remembered folder is still there is a question for the shell, not for this file, so it is asked rather than answered here.
    public static async Task<bool> ForgetMissingFoldersAsync(Settings settings, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var names = settings.RecentFolders.Select(f => f.ParsingName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0)
            return false;

        var missing = await Task.Run(() =>
        {
            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (!exists(name))
                {
                    gone.Add(name);
                }
            }

            return gone;
        }).ConfigureAwait(true);

        return settings.RecentFolders.RemoveAll(f => missing.Contains(f.ParsingName)) > 0;
    }
}
