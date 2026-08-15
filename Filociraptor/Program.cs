namespace Filociraptor;

internal static class Program
{
    private static readonly string _defaultPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private const string _benchCommand = "bench";
    private const int _defaultRuns = 3;

    [STAThread] // necessary for some shell APIs to work properly
    private static void Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : string.Empty;
        if (string.Equals(command, _benchCommand, StringComparison.OrdinalIgnoreCase))
        {
            var path = args.Length > 1 ? args[1] : _defaultPath;
            var runs = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : _defaultRuns;
            Benchmark.RunAsync(path, runs).Wait();
            return;
        }

        RunWindow(args.Length > 0 ? args[0] : _defaultPath);
    }

    private static void RunWindow(string path)
    {
        using var app = new Application();

        // makes every await in the navigation pipeline come back on the UI thread through the message queue.
        WindowSynchronizationContext.Install();

        using var window = new MainWindow();
        var monitor = window.GetMonitor(MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST)!;
        window.ResizeClient(monitor.WorkingArea.Width * 3 / 4, monitor.WorkingArea.Height * 3 / 4);
        window.Center();
        window.Show();
        window.SetForeground();
        window.Navigate(path);
        window.LoadDrives();
        app.Run();
    }
}
