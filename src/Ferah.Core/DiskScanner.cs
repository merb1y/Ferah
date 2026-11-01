using System.Diagnostics;

namespace Ferah.Core;

public static class DiskScanner
{
    public static LargeFileScan Scan(string root, long minimumBytes, int limit, CancellationToken token,
        IProgress<ScanProgress>? progress = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Seçilen klasör bulunamadı.");
        if (minimumBytes < 0 || limit is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(limit));
        root = PathSafety.Normalize(root);
        var watch = Stopwatch.StartNew();
        var reportAt = TimeSpan.Zero;
        var walker = new FileWalker();
        var largest = new PriorityQueue<DiskFile, long>();
        long examined = 0, bytes = 0;
        var cancelled = false;
        try
        {
            foreach (var file in walker.Enumerate(root, token))
            {
                examined++;
                bytes += file.Size;
                if (file.Size >= minimumBytes)
                {
                    largest.Enqueue(file, file.Size);
                    if (largest.Count > limit) largest.Dequeue();
                }
                if (watch.Elapsed - reportAt >= TimeSpan.FromMilliseconds(150))
                {
                    progress?.Report(new(examined, bytes, walker.Skipped, file.Path));
                    reportAt = watch.Elapsed;
                }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        progress?.Report(new(examined, bytes, walker.Skipped, ""));
        return new(root, largest.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(file => file.Size).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            examined, bytes, walker.Skipped, cancelled, watch.Elapsed);
    }
}
