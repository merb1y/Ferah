using System.Diagnostics;

namespace Ferah.Core;

public sealed class CleanupService
{
    private readonly IReadOnlyList<CleanupRule> _rules;
    private readonly Func<string, bool> _isRunning;
    private readonly object _gate = new();
    private CleanupScan? _lastScan;

    public CleanupService(IReadOnlyList<CleanupRule> rules, Func<string, bool>? isRunning = null)
    {
        _rules = rules;
        _isRunning = isRunning ?? IsProcessRunning;
        if (rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            throw new ArgumentException("Temizlik kategori kimlikleri benzersiz olmalı.", nameof(rules));
        foreach (var root in rules.SelectMany(rule => rule.Roots))
            if (!PathSafety.IsLocal(root) || string.Equals(PathSafety.Normalize(root), Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Temizlik kökü yerel bir alt klasör olmalı.", nameof(rules));
        if (rules.Any(rule => rule.MinimumAgeDays < 1))
            throw new ArgumentException("Yeni dosyaları korumak için bir yaş sınırı gerekli.", nameof(rules));
    }

    public CleanupScan Scan(CancellationToken token, IProgress<ScanProgress>? progress = null)
    {
        lock (_gate)
        {
            _lastScan = null;
            var scan = BuildScan(token, progress);
            if (!scan.Cancelled) _lastScan = scan;
            return scan;
        }
    }

    private CleanupScan BuildScan(CancellationToken token, IProgress<ScanProgress>? progress)
    {
        var watch = Stopwatch.StartNew();
        var reportAt = TimeSpan.Zero;
        var now = DateTime.UtcNow;
        var walker = new FileWalker();
        var groups = new List<CleanupGroup>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long examined = 0, bytes = 0;
        var extraSkipped = 0;
        var cancelled = false;
        var candidateCount = 0;

        foreach (var rule in _rules)
        {
            var candidates = new List<FileCandidate>();
            string? note = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (rule.BlockingProcess is not null && _isRunning(rule.BlockingProcess))
                {
                    note = "Tarayıcı açık; kapatıp yeniden tarayın.";
                    groups.Add(new(rule, candidates, note));
                    continue;
                }
                if (!rule.Roots.Any(Directory.Exists)) note = "Bu bilgisayarda temizlenecek klasör bulunamadı.";
                var beforeSkipped = walker.Skipped + extraSkipped;
                foreach (var root in rule.Roots)
                {
                    foreach (var file in walker.Enumerate(root, token))
                    {
                        if (!visited.Add(file.Path)) continue;
                        examined++;
                        if (watch.Elapsed - reportAt >= TimeSpan.FromMilliseconds(150))
                        {
                            progress?.Report(new(examined, bytes, walker.Skipped + extraSkipped, file.Path));
                            reportAt = watch.Elapsed;
                        }
                        if (file.LastWriteUtc > now.AddDays(-rule.MinimumAgeDays) || file.CreationUtc > now.AddDays(-rule.MinimumAgeDays) ||
                            (file.Attributes & (FileAttributes.System | FileAttributes.ReadOnly)) != 0) continue;
                        if (++candidateCount > 200_000)
                            throw new InvalidOperationException("200.000 dosyalık güvenli tarama sınırına ulaşıldı. Hiçbir dosya silinmedi.");
                        try
                        {
                            var identity = WindowsFileAccess.ReadIdentity(file.Path);
                            candidates.Add(new(rule.Id, PathSafety.Normalize(root), file.Path, file.Size, file.LastWriteUtc, file.CreationUtc, identity));
                            bytes += file.Size;
                        }
                        catch (Exception error) when (FileWalker.IsAccessError(error)) { extraSkipped++; }
                    }
                }
                if (walker.Skipped + extraSkipped > beforeSkipped)
                    note = "Kilitli, erişilemeyen veya bağlantı içeren öğeler atlandı.";
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception error) when (FileWalker.IsAccessError(error))
            {
                extraSkipped++;
                note = "Klasöre erişilemedi; dosyalarda değişiklik yapılmadı.";
            }
            groups.Add(new(rule, candidates.AsReadOnly(), note));
            if (cancelled) break;
        }

        foreach (var rule in _rules.Where(rule => groups.All(group => group.Rule.Id != rule.Id)))
            groups.Add(new(rule, [], "Tarama durduruldu; bu kategori taranmadı."));
        progress?.Report(new(examined, bytes, walker.Skipped + extraSkipped, ""));
        return new(now, groups.AsReadOnly(), examined, walker.Skipped + extraSkipped, cancelled, watch.Elapsed);
    }

    public CleanupResult Clean(CleanupScan scan, IReadOnlySet<string> selected, CancellationToken token,
        IProgress<ScanProgress>? progress = null)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(scan, _lastScan) || scan.Cancelled)
                throw new InvalidOperationException("Bu tarama geçersiz, durdurulmuş veya daha önce kullanılmış. Yeniden tarayın.");
            if (selected.Count == 0 || selected.Any(id => !_rules.Any(rule => rule.Id == id)))
                throw new ArgumentException("En az bir geçerli kategori seçin.", nameof(selected));
            _lastScan = null;
            return ExecuteClean(scan, selected, token, progress);
        }
    }

    private CleanupResult ExecuteClean(CleanupScan scan, IReadOnlySet<string> selected, CancellationToken token,
        IProgress<ScanProgress>? progress)
    {
        if (DateTime.UtcNow - scan.CreatedUtc > TimeSpan.FromMinutes(30))
            throw new InvalidOperationException("Tarama 30 dakikadan eski. Güvenlik için yeniden tarayın.");

        var issues = new List<OperationIssue>();
        var deleted = 0;
        var skipped = 0;
        long bytes = 0;
        var cancelled = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watch = Stopwatch.StartNew();
        var reportedAt = TimeSpan.Zero;
        foreach (var group in scan.Groups.Where(group => selected.Contains(group.Rule.Id)))
        {
            var rule = _rules.SingleOrDefault(item => item.Id == group.Rule.Id)
                ?? throw new InvalidOperationException("Bilinmeyen temizlik kategorisi.");
            var nextProcessCheck = DateTime.MinValue;
            var blocked = false;
            foreach (var file in group.Files)
            {
                if (token.IsCancellationRequested) { cancelled = true; break; }
                if (!seen.Add(file.Path)) continue;
                try
                {
                    if (file.RuleId != rule.Id || !rule.Roots.Any(root =>
                        string.Equals(PathSafety.Normalize(root), file.Root, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Dosya izin verilen kategoriye ait değil.");
                    if (rule.BlockingProcess is not null && DateTime.UtcNow >= nextProcessCheck)
                    {
                        blocked = _isRunning(rule.BlockingProcess);
                        nextProcessCheck = DateTime.UtcNow.AddMilliseconds(400);
                    }
                    if (blocked) throw new IOException("Tarayıcı açık; önbelleği korunuyor.");
                    WindowsFileAccess.DeleteVerified(file, DateTime.UtcNow.AddDays(-rule.MinimumAgeDays));
                    deleted++;
                    bytes += file.Size;
                }
                catch (Exception error) when (FileWalker.IsAccessError(error) || error is ArgumentException)
                {
                    skipped++;
                    if (issues.Count < 100) issues.Add(new(file.Path, error.Message));
                }
                if (watch.Elapsed - reportedAt >= TimeSpan.FromMilliseconds(150))
                {
                    progress?.Report(new(deleted, bytes, skipped, file.Path));
                    reportedAt = watch.Elapsed;
                }
            }
            if (cancelled) break;
        }
        return new(deleted, bytes, skipped, cancelled, issues.AsReadOnly());
    }

    private static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
