namespace Filociraptor;

internal static class Program
{
    private static readonly string _defaultPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private const string _benchCommand = "bench";
    private const string _positionArgument = "position";
    private const int _defaultRuns = 3;

    [STAThread] // necessary for some shell APIs to work properly
    private static void Main()
    {
        var commandLine = CommandLine.Current;
        var first = commandLine.GetNullifiedArgument(0);
        if (_benchCommand.EqualsIgnoreCase(first))
        {
            var benchPath = commandLine.GetNullifiedArgument(1) ?? _defaultPath;
            var runs = commandLine.GetArgument(2, _defaultRuns);
            Benchmark.RunAsync(benchPath, runs).Wait();
            return;
        }

        // the first argument is a parsing name, and nothing asked for means where it was left last time.
        var settings = SettingsFile.Load();
        var path = first ?? settings.RecentFolders.FirstOrDefault()?.ParsingName ?? _defaultPath;
        RunWindow(settings, path, commandLine.GetNullifiedArgument(_positionArgument));
    }

    private static void RunWindow(Settings settings, string path, string? position)
    {
        using var app = new Application();

        // makes every await in the navigation pipeline come back on the UI thread through the message queue.
        WindowSynchronizationContext.Install();

        using var window = new MainWindow(settings);
        window.IsPlacing = true;
        try
        {
            if (!TryPlaceNextTo(window, position) && !TryRestore(window, settings))
            {
                var monitor = window.GetMonitor(MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST)!;
                window.ResizeClient(monitor.WorkingArea.Width * 3 / 4, monitor.WorkingArea.Height * 3 / 4);
                window.Center();
            }
        }
        finally
        {
            window.IsPlacing = false;
        }

        window.Show();
        window.SetForeground();
        window.Navigate(path);
        window.LoadDrives();
        app.Run();
    }

    private static bool TryRestore(MainWindow window, Settings settings) => WindowPosition.TryParse(settings.Window, out var saved) && saved.Restore(window);
    private static bool TryPlaceNextTo(MainWindow window, string? position)
    {
        if (!RECT.TryParse(position, null, out var origin) || origin.Width <= 0 || origin.Height <= 0)
            return false;

        window.ResizeAndMove(origin);

        var step = Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYCAPTION);
        var work = window.GetMonitor(MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST)!.WorkingArea;

        var left = origin.left + step;
        var top = origin.top + step;

        // stepped off the far edge, so it starts again at the near one rather than opening out of sight.
        if (left + origin.Width > work.right)
        {
            left = work.left;
        }

        if (top + origin.Height > work.bottom)
        {
            top = work.top;
        }

        window.ResizeAndMove(left, top, origin.Width, origin.Height);
        return true;
    }
}
