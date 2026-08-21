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

        // the first argument is a parsing name
        RunWindow(first ?? _defaultPath, commandLine.GetNullifiedArgument(_positionArgument));
    }

    private static void RunWindow(string path, string? position)
    {
        using var app = new Application();

        // makes every await in the navigation pipeline come back on the UI thread through the message queue.
        WindowSynchronizationContext.Install();

        using var window = new MainWindow();
        if (!TryPlaceNextTo(window, position))
        {
            var monitor = window.GetMonitor(MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST)!;
            window.ResizeClient(monitor.WorkingArea.Width * 3 / 4, monitor.WorkingArea.Height * 3 / 4);
            window.Center();
        }

        window.Show();
        window.SetForeground();
        window.Navigate(path);
        window.LoadDrives();
        app.Run();
    }

    // another instance was started by one of our windows, and it says where it was, so this one opens beside it rather than exactly on top of it, the way a cascade does.
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
