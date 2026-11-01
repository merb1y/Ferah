using Ferah.Core;

namespace Ferah.App;

public sealed class CleanupItem(CleanupRule rule) : ObservableObject
{
    private bool _selected = rule.Recommended;
    private CleanupGroup? _group;
    public CleanupRule Rule { get; } = rule;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Title => Rule.Title;
    public string Description => Rule.Description;
    public string Icon => Rule.Icon;
    public string Age => $"{Rule.MinimumAgeDays}+ gün";
    public string Badge => Rule.RequiresAdmin ? "Yönetici erişimi gerekebilir" : Rule.Recommended ? "Önerilen" : "İsteğe bağlı";
    public long Bytes => _group?.Bytes ?? 0;
    public int Count => _group?.Files.Count ?? 0;
    public string Size => _group is null ? "—" : Formatting.Bytes(Bytes);
    public string CountText => _group is null ? "Taranmadı" : $"{Formatting.Count(Count)} dosya";
    public string? Note => _group?.Note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public string Roots => Rule.Roots.Count == 0 ? "Profil bulunamadı." : string.Join(Environment.NewLine, Rule.Roots);
    public void Update(CleanupGroup? group)
    {
        _group = group;
        NotifyMany(nameof(Bytes), nameof(Count), nameof(Size), nameof(CountText), nameof(Note), nameof(HasNote));
    }
}

public sealed class LargeFileItem(CapturedDiskFile captured) : ObservableObject
{
    private bool _selected;
    public CapturedDiskFile Captured { get; } = captured;
    public bool Selected { get => _selected; set => Set(ref _selected, CanRecycle && value); }
    public bool CanRecycle => Captured.BlockReason is null && Captured.Identity is not null;
    public string Path => Captured.File.Path;
    public string Name => Captured.File.Name;
    public string Folder => Captured.File.Folder;
    public long Bytes => Captured.File.Size;
    public string Size => Formatting.Bytes(Bytes);
    public string Status => CanRecycle ? "İncelenebilir" : "Korumalı";
    public string? BlockReason => Captured.BlockReason;
    public string Kind => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => "Video",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Arşiv",
        ".iso" or ".img" or ".vhd" or ".vhdx" or ".vmdk" => "Disk imajı",
        ".exe" or ".msi" or ".msix" => "Uygulama",
        ".pdf" or ".docx" or ".xlsx" or ".pptx" => "Belge",
        ".png" or ".jpg" or ".jpeg" or ".heic" or ".psd" => "Görsel",
        ".mp3" or ".wav" or ".flac" => "Ses",
        _ => "Diğer"
    };
    public string Icon => Kind switch
    {
        "Video" => "\uE714", "Arşiv" => "\uF012", "Disk imajı" => "\uE958",
        "Uygulama" => "\uE71D", "Görsel" => "\uEB9F", "Ses" => "\uE8D6", _ => "\uE8A5"
    };
}

public sealed class TweakItem(TweakDefinition definition) : ObservableObject
{
    private bool _selected;
    private TweakStatus? _status;
    public TweakDefinition Definition { get; } = definition;
    public bool Selected { get => _selected; set => Set(ref _selected, CanChange && value); }
    public bool CanChange => _status is { Available: true, Applied: false };
    public bool Applied => _status?.Applied ?? false;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Warning => Definition.Warning;
    public string Badge => Definition.Badge;
    public string Icon => Definition.Icon;
    public string State => _status?.Detail ?? "Kontrol ediliyor…";
    public void Update(TweakStatus status)
    {
        _status = status;
        if (!CanChange) Selected = false;
        NotifyMany(nameof(CanChange), nameof(Applied), nameof(State));
    }
}

public sealed record BackupItem(TweakBackup Backup)
{
    public string Display => $"{Formatting.Date(Backup.CreatedUtc)} · {Backup.Status}";
    public string Detail => string.Join(", ", TweakCatalog.All.Where(tweak => Backup.TweakIds.Contains(tweak.Id)).Select(tweak => tweak.Title));
    public bool CanRestore => Backup.Status != "Geri alındı";
}

public sealed record HistoryItem(HistoryEntry Entry)
{
    public string Date => Formatting.Date(Entry.TimestampUtc);
    public string Title => Entry.Title;
    public string Detail => Entry.Detail;
    public string Status => Entry.Status;
    public string Size => Entry.Bytes == 0 ? "—" : Formatting.Bytes(Entry.Bytes);
}

public sealed record SizeOption(string Label, long Bytes)
{
    public override string ToString() => Label;
}
public sealed record DriveItem(DriveSnapshot Drive)
{
    public string Name => Drive.Display;
    public string Free => $"{Formatting.Bytes(Drive.Free)} boş / {Formatting.Bytes(Drive.Total)}";
    public double Percent => Drive.UsedPercent;
}
