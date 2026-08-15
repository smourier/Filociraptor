namespace Filociraptor.Diagnostics;

// the numbers this project is about.
// a WinExe has no console, and borrowing the one that launched it puts the output after the prompt has already come back, where it is easily missed.
// so the report is gathered and shown when it is finished.
internal static class Benchmark
{
    private const int _megaByte = 1024 * 1024;

    public static async Task RunAsync(string path, int runs)
    {
        var report = new StringBuilder();
        report.AppendLine("path " + path);
        report.AppendLine();

        for (var run = 1; run <= runs; run++)
        {
            await RunOnceAsync(report, path, run).ConfigureAwait(false);
        }

        MessageBox.Show(report.ToString(), "Filociraptor benchmark");
    }

    private static async Task RunOnceAsync(StringBuilder report, string path, int run)
    {
        using var items = new FolderItems();
        var allocatedBefore = GC.GetTotalAllocatedBytes(false);
        var gen0Before = GC.CollectionCount(0);
        var start = Stopwatch.GetTimestamp();
        var firstBatch = 0d;
        var batches = 0;

        await foreach (var count in DirectoryScanner.ScanAsync(path, items, true, CancellationToken.None).ConfigureAwait(false))
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

        report.AppendLine($"run {run}");
        report.AppendLine($"  enumerate: {items.Count} items in {scan:F1} ms, first batch at {firstBatch:F1} ms");

        if (items.Count > 0)
        {
            var rate = items.Count / Math.Max(scan, 0.001) * 1000;
            report.AppendLine($"  rate: {rate:F0} items/s");
        }

        foreach (var column in Enum.GetValues<SortColumn>())
        {
            var sortStart = Stopwatch.GetTimestamp();
            items.Sort(column, false);
            var sort = Milliseconds(sortStart);
            report.AppendLine($"  sort by {column}: {sort:F1} ms (keys {items.KeyMilliseconds:F1}, primary {items.PrimarySortMilliseconds:F1}, refine {items.RefineMilliseconds:F1})");
        }

        report.AppendLine($"  buffers: {items.BufferBytes / _megaByte} MB, working set {Environment.WorkingSet / _megaByte} MB");
        report.AppendLine($"  allocated while scanning: {scanAllocated / 1024} KB, {GC.CollectionCount(0) - gen0Before} collections");
        report.AppendLine();
    }

    private static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
}
