namespace Filociraptor;

internal static class Program
{
    private const string _defaultPath = @"C:\Windows\System32";
    private const string _benchCommand = "bench";
    private const string _selfTestCommand = "selftest";
    private const int _defaultRuns = 3;
    private const int _selfTestWidth = 1280;
    private const int _selfTestHeight = 800;
    private const int _selfTestFrames = 30;

    private static async Task Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : string.Empty;
        if (string.Equals(command, _benchCommand, StringComparison.OrdinalIgnoreCase))
        {
            var path = args.Length > 1 ? args[1] : _defaultPath;
            var runs = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : _defaultRuns;
            await Benchmark.RunAsync(path, runs).ConfigureAwait(false);
            return;
        }

        if (string.Equals(command, _selfTestCommand, StringComparison.OrdinalIgnoreCase))
        {
            RunSelfTest(args.Length > 1 ? args[1] : _defaultPath);
            return;
        }

        RunWindow(args.Length > 0 ? args[0] : _defaultPath);
    }

    // drives the whole graphics path once without a message loop, so a broken device or a broken frame is a
    // failed command rather than something only a human staring at a window would notice.
    private static void RunSelfTest(string path)
    {
        Benchmark.AttachToConsole();
        Console.WriteLine("Filociraptor self test");
        Console.WriteLine("path " + path);

        // the window procedure swallows exceptions into the application error list, so a failure during creation is
        // otherwise invisible from a command line.
        Application.CanShowFatalError = false;
        Application.ShowFatalErrorsOnUnhandledException = false;

        using var app = new Application();
        using var window = new MainWindow();
        window.ResizeClient(_selfTestWidth, _selfTestHeight);

        var failed = false;

        // the device is built when the window reports itself created, and that arrives as a posted message,
        // so the whole test has to run from inside the message loop.
        window.Created += (sender, e) =>
        {
            failed |= !Report("device", window.HasDevice);

            window.LoadSynchronously(path);
            Console.WriteLine($"items         {window.ItemCount}");
            failed |= !Report("scan", window.ItemCount > 0);

            var rendered = true;
            for (var i = 0; i < _selfTestFrames; i++)
            {
                rendered &= window.RenderOnce();
            }

            failed |= !Report("render", rendered);

            // a frame that never reached the device takes no measurable time, which is the shape of a silent failure.
            failed |= !Report("frame", window.LastFrameMilliseconds > 0);
            Console.WriteLine($"frame         {window.LastFrameMilliseconds:F2} ms");
            app.Exit();
        };

        window.Show();
        app.Run();

        foreach (var error in Application.GetErrors())
        {
            failed = true;
            Console.WriteLine();
            Console.WriteLine(error.ToString());
        }

        Console.WriteLine(failed ? "SELF TEST FAILED" : "self test passed");
        Environment.ExitCode = failed ? 1 : 0;
    }

    private static bool Report(string name, bool ok)
    {
        Console.WriteLine($"{name,-14}{(ok ? "ok" : "FAILED")}");
        return ok;
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
        app.Run();
    }
}
