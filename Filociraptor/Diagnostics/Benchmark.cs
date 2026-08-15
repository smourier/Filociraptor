namespace Filociraptor.Diagnostics;

// headless mode, so the numbers this project is about can be produced without a window in the way,
// and compared run to run from a script.
internal static class Benchmark
{
    private const uint _attachParentProcess = 0xFFFFFFFF;
    private const int _megaByte = 1024 * 1024;

    public static async Task RunAsync(string path, int runs)
    {
        AttachToConsole();

        Console.WriteLine("Filociraptor benchmark");
        Console.WriteLine("path " + path);
        Console.WriteLine();

        for (var run = 1; run <= runs; run++)
        {
            await RunOnceAsync(path, run).ConfigureAwait(false);
        }
    }

    private static async Task RunOnceAsync(string path, int run)
    {
        using var items = new FolderItems();
        var allocatedBefore = GC.GetTotalAllocatedBytes(false);
        var gen0Before = GC.CollectionCount(0);
        var start = Stopwatch.GetTimestamp();
        var firstBatch = 0d;
        var batches = 0;

        await foreach (var count in DirectoryScanner.ScanAsync(path, items, CancellationToken.None).ConfigureAwait(false))
        {
            if (batches == 0)
            {
                firstBatch = Milliseconds(start);
            }

            batches++;
            _ = count;
        }

        var scan = Milliseconds(start);
        var scanAllocated = GC.GetTotalAllocatedBytes(false) - allocatedBefore;

        Console.WriteLine($"run {run}");
        Console.WriteLine($"  enumerate     {items.Count} items in {scan:F1} ms, first batch at {firstBatch:F1} ms");

        if (items.Count > 0)
        {
            var rate = items.Count / Math.Max(scan, 0.001) * 1000;
            Console.WriteLine($"  rate          {rate:F0} items/s");
        }

        foreach (var column in Enum.GetValues<SortColumn>())
        {
            var sortStart = Stopwatch.GetTimestamp();
            items.Sort(column, false);
            var sort = Milliseconds(sortStart);
            Console.WriteLine($"  sort {column,-9}{sort,6:F1} ms  (keys {items.KeyMilliseconds:F1}, primary {items.PrimarySortMilliseconds:F1}, refine {items.RefineMilliseconds:F1}, {items.ComparerCalls} comparer calls)");
        }

        Console.WriteLine($"  buffers       {items.BufferBytes / _megaByte} MB");
        Console.WriteLine($"  working set   {Environment.WorkingSet / _megaByte} MB");
        Console.WriteLine($"  scan alloc    {scanAllocated / 1024} KB, {GC.CollectionCount(0) - gen0Before} gen0 collections");
        Console.WriteLine();
    }

    private static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

    // a WinExe has no console of its own, so borrow the one that launched it.
    public static void AttachToConsole()
    {
        if (!Functions.AttachConsole(_attachParentProcess))
        {
            Functions.AllocConsole();
        }

        var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(output);
    }
}
