namespace Ferah.Core;

public sealed record FileIdentity(uint Volume, ulong FileId);

public sealed record DiskFile(string Path, long Size, DateTime LastWriteUtc, FileAttributes Attributes, DateTime CreationUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? Path;
}

public sealed record FileCandidate(
    string RuleId, string Root, string Path, long Size,
    DateTime LastWriteUtc, DateTime CreationUtc, FileIdentity Identity);

public sealed record CleanupRule(
    string Id, string Title, string Description, string Icon,
    IReadOnlyList<string> Roots, int MinimumAgeDays,
    bool Recommended = true, bool RequiresAdmin = false,
    string? BlockingProcess = null);

public sealed record CleanupGroup(
    CleanupRule Rule, IReadOnlyList<FileCandidate> Files, string? Note)
{
    public long Bytes => Files.Sum(file => file.Size);
}

public sealed record ScanProgress(long Files, long Bytes, int Skipped, string CurrentPath);

public sealed record CleanupScan(
    DateTime CreatedUtc, IReadOnlyList<CleanupGroup> Groups,
    long ExaminedFiles, int Skipped, bool Cancelled, TimeSpan Duration);

public sealed record OperationIssue(string Path, string Reason);

public sealed record CleanupResult(
    int Deleted, long DeletedBytes, int Skipped, bool Cancelled,
    IReadOnlyList<OperationIssue> Issues);

public sealed record LargeFileScan(
    string Root, IReadOnlyList<DiskFile> Files, long ExaminedFiles,
    long ExaminedBytes, int Skipped, bool Cancelled, TimeSpan Duration);

public sealed record DriveSnapshot(string Name, string Label, long Total, long Free)
{
    public long Used => Total - Free;
    public double UsedPercent => Total == 0 ? 0 : Used * 100d / Total;
    public string Display => $"{Name}  {Label}".Trim();
    public override string ToString() => Display;
}

public sealed record MachineSnapshot(
    string Windows, string Computer, int LogicalProcessors,
    ulong TotalMemory, ulong AvailableMemory, double CpuPercent,
    IReadOnlyList<DriveSnapshot> Drives);

public sealed record HistoryEntry(
    string Id, DateTime TimestampUtc, string Title,
    string Detail, string Status, long Bytes = 0);
