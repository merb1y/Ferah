using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Data;
using System.Windows.Input;
using Ferah.Core;

namespace Ferah.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUserInteraction _interaction;
    private readonly CleanupService _cleanup;
    private readonly MachineInfoService _machine = new();
    private readonly SettingsService _settings;
    private readonly FileBackupStore _backups;
    private readonly HistoryStore _history;
    private readonly string _dataFolder;
    private readonly string _userMode;
    private CancellationTokenSource? _cancellation;
    private CleanupScan? _cleanupScan;
    private LargeFileScan? _largeScan;
    private MachineSnapshot? _snapshot;
    private string _page = "Overview";
    private string _status = "Hazır · Hiçbir dosya veya sistem ayarı değiştirilmedi.";
    private bool _busy, _canCancel, _refreshing;
    private string _scanPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private DriveSnapshot? _selectedDrive;
    private string _search = "";
    private SizeOption _minimumSize;
    private BackupItem? _selectedBackup;
    private string _cleanupHeadline = "Önce bir göz atalım.";
    private string _cleanupDetail = "Tarama yalnızca dosyaları listeler; hiçbir şeyi silmez.";
    private string _largeDetail = "Bir sürücü veya klasör seçerek başlayın. En büyük 500 sonuç gösterilir.";

    public MainViewModel(IUserInteraction interaction)
    {
        _interaction = interaction;
        _userMode = GetUserMode();
        _dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerahDesktop");
        _backups = new(_dataFolder);
        _history = new(_dataFolder);
        _settings = new(new WindowsSettingsBackend(), _backups);
        var rules = CleanupCatalog.Create();
        _cleanup = new(rules);
        foreach (var rule in rules)
        {
            var item = new CleanupItem(rule);
            item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(CleanupItem.Selected)) SelectionChanged(); };
            CleanupItems.Add(item);
        }
        foreach (var definition in TweakCatalog.All)
        {
            var item = new TweakItem(definition);
            item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(TweakItem.Selected)) SelectionChanged(); };
            Tweaks.Add(item);
        }
        _minimumSize = SizeOptions[2];
        LargeFilesView = CollectionViewSource.GetDefaultView(LargeFiles);
        LargeFilesView.Filter = value => value is LargeFileItem file &&
            (string.IsNullOrWhiteSpace(Search) || file.Path.Contains(Search, StringComparison.OrdinalIgnoreCase));

        NavigateCommand = new RelayCommand(parameter => Page = parameter?.ToString() ?? "Overview");
        ScanCleanupCommand = Async(ScanCleanupAsync);
        CleanCommand = Async(CleanAsync, () => _cleanupScan is not null && SelectedCleanupCount > 0);
        ScanLargeCommand = Async(ScanLargeAsync);
        PickFolderCommand = new RelayCommand(_ => PickFolder(), _ => IsIdle);
        RecycleCommand = Async(RecycleAsync, () => _largeScan is not null && SelectedLargeCount > 0);
        ExportLargeCommand = Async(ExportLargeAsync, () => LargeFiles.Count > 0);
        ApplyTweaksCommand = Async(ApplyTweaksAsync, () => SelectedTweakCount > 0);
        SelectRecommendedCommand = new RelayCommand(_ =>
        {
            foreach (var tweak in Tweaks) tweak.Selected = tweak.Definition.Recommended;
        }, _ => IsIdle);
        SelectSafeCleanupCommand = new RelayCommand(_ =>
        {
            foreach (var item in CleanupItems) item.Selected = item.Rule.Recommended;
        }, _ => IsIdle);
        RestoreCommand = Async(RestoreAsync, () => SelectedBackup?.CanRestore == true);
        RefreshSettingsCommand = Async(RefreshSettingsAsync);
        RefreshHistoryCommand = Async(RefreshHistoryAsync);
        ExportHistoryCommand = Async(ExportHistoryAsync, () => History.Count > 0);
        OpenLocationCommand = new RelayCommand(OpenLocation, _ => IsIdle);
        OpenToolCommand = new RelayCommand(OpenTool, _ => IsIdle);
        CancelCommand = new RelayCommand(_ =>
        {
            _cancellation?.Cancel();
            Status = "Durduruluyor · Devam eden dosya işleminin tamamlanması bekleniyor…";
            CanCancel = false;
        }, _ => CanCancel);
    }

    private AsyncCommand Async(Func<Task> execute, Func<bool>? condition = null) =>
        new(execute, () => IsIdle && (condition?.Invoke() ?? true), ReportError);

    public ObservableCollection<CleanupItem> CleanupItems { get; } = [];
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = [];
    public ICollectionView LargeFilesView { get; }
    public ObservableCollection<TweakItem> Tweaks { get; } = [];
    public ObservableCollection<BackupItem> Backups { get; } = [];
    public ObservableCollection<HistoryItem> History { get; } = [];
    public ObservableCollection<DriveSnapshot> Drives { get; } = [];
    public ObservableCollection<DriveItem> DriveCards { get; } = [];
    public IReadOnlyList<SizeOption> SizeOptions { get; } =
        [new("1 MB ve üzeri", 1024 * 1024), new("10 MB ve üzeri", 10 * 1024 * 1024),
         new("100 MB ve üzeri", 100 * 1024 * 1024), new("500 MB ve üzeri", 500 * 1024 * 1024), new("1 GB ve üzeri", 1024L * 1024 * 1024)];

    public ICommand NavigateCommand { get; }
    public ICommand ScanCleanupCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand ScanLargeCommand { get; }
    public ICommand PickFolderCommand { get; }
    public ICommand RecycleCommand { get; }
    public ICommand ExportLargeCommand { get; }
    public ICommand ApplyTweaksCommand { get; }
    public ICommand SelectRecommendedCommand { get; }
    public ICommand SelectSafeCleanupCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand RefreshSettingsCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand ExportHistoryCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand OpenToolCommand { get; }
    public ICommand CancelCommand { get; }

    public string Page
    {
        get => _page;
        private set
        {
            if (!Set(ref _page, value)) return;
            NotifyMany(nameof(PageTitle), nameof(PageSubtitle), nameof(IsOverview), nameof(IsCleanup), nameof(IsLarge), nameof(IsPerformance), nameof(IsHistory));
        }
    }
    public bool IsOverview => Page == "Overview";
    public bool IsCleanup => Page == "Cleanup";
    public bool IsLarge => Page == "Large";
    public bool IsPerformance => Page == "Performance";
    public bool IsHistory => Page == "History";
    public string PageTitle => Page switch
    {
        "Cleanup" => "Disk temizliği", "Large" => "Büyük dosyalar", "Performance" => "Performans ayarları",
        "History" => "İşlem geçmişi", _ => "Genel bakış"
    };
    public string PageSubtitle => Page switch
    {
        "Cleanup" => "Gereksiz birikintileri bul. Ne silineceğine sen karar ver.",
        "Large" => "Disk alanının nereye gittiğini keşfet.",
        "Performance" => "Daha sade bir deneyim. Açıklamalı ve geri alınabilir ayarlar.",
        "History" => "Yapılan işlemler, sonuçlar ve yerel ayar yedekleri.",
        _ => "Bilgisayarına biraz nefes aldır."
    };
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { Notify(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool IsIdle => !IsBusy;
    public bool CanCancel { get => _canCancel; private set { Set(ref _canCancel, value); CommandManager.InvalidateRequerySuggested(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ScanPath { get => _scanPath; private set => Set(ref _scanPath, value); }
    public DriveSnapshot? SelectedDrive
    {
        get => _selectedDrive;
        set { if (Set(ref _selectedDrive, value) && value is not null) ScanPath = value.Name; }
    }
    public SizeOption MinimumSize { get => _minimumSize; set => Set(ref _minimumSize, value); }
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) { LargeFilesView.Refresh(); Notify(nameof(VisibleLargeCount)); } }
    }
    public BackupItem? SelectedBackup
    {
        get => _selectedBackup;
        set { Set(ref _selectedBackup, value); Notify(nameof(BackupDetail)); CommandManager.InvalidateRequerySuggested(); }
    }
    public string BackupDetail => SelectedBackup?.Detail ?? "İlk ayar değişikliğinden önce yedek otomatik oluşturulur.";
    public string CleanupHeadline { get => _cleanupHeadline; private set => Set(ref _cleanupHeadline, value); }
    public string CleanupDetail { get => _cleanupDetail; private set => Set(ref _cleanupDetail, value); }
    public string LargeDetail { get => _largeDetail; private set => Set(ref _largeDetail, value); }
    public string SelectedCleanupSize => Formatting.Bytes(CleanupItems.Where(item => item.Selected).Sum(item => item.Bytes));
    public int SelectedCleanupCount => CleanupItems.Where(item => item.Selected).Sum(item => item.Count);
    public string SelectedCleanupText => $"{Formatting.Count(SelectedCleanupCount)} dosya · {SelectedCleanupSize} seçili";
    public int SelectedLargeCount => LargeFiles.Count(file => file.Selected);
    public string SelectedLargeText => $"{Formatting.Count(SelectedLargeCount)} dosya · {Formatting.Bytes(LargeFiles.Where(file => file.Selected).Sum(file => file.Bytes))} seçili";
    public int SelectedTweakCount => Tweaks.Count(tweak => tweak.Selected && tweak.CanChange);
    public string SelectedTweakText => $"{SelectedTweakCount} ayar seçili";
    public string VisibleLargeCount => $"{Formatting.Count(LargeFilesView.Cast<object>().Count())} sonuç";
    public bool LargeEmpty => LargeFiles.Count == 0;
    public bool HistoryEmpty => History.Count == 0;
    public string ExaminedFilesText => Formatting.Count(_largeScan?.ExaminedFiles ?? 0);
    public string ExaminedBytesText => Formatting.Bytes(_largeScan?.ExaminedBytes ?? 0);
    public string SkippedText => Formatting.Count(_largeScan?.Skipped ?? 0);
    private DriveSnapshot? PrimaryDrive => _snapshot is { Drives.Count: > 0 } ? _snapshot.Drives[0] : null;
    public string DiskFree => PrimaryDrive is null ? "—" : Formatting.Bytes(PrimaryDrive.Free);
    public string DiskDetail => PrimaryDrive is null ? "Sürücüler kontrol ediliyor…" : $"{PrimaryDrive.Name} · {Formatting.Bytes(PrimaryDrive.Total)} toplam";
    public double DiskUsedPercent => PrimaryDrive?.UsedPercent ?? 0;
    public string MemoryPercent => _snapshot is null || _snapshot.TotalMemory == 0 ? "—" : $"%{MemoryUsedPercent:N0}";
    public double MemoryUsedPercent => _snapshot is null || _snapshot.TotalMemory == 0 ? 0 :
        (_snapshot.TotalMemory - _snapshot.AvailableMemory) * 100d / _snapshot.TotalMemory;
    public string MemoryDetail => _snapshot is null ? "Bellek kontrol ediliyor…" :
        $"{Formatting.Bytes((long)_snapshot.AvailableMemory)} kullanılabilir / {Formatting.Bytes((long)_snapshot.TotalMemory)}";
    public double CpuPercent => _snapshot?.CpuPercent ?? 0;
    public string CpuText => _snapshot is null ? "—" : $"%{CpuPercent:N0}";
    public string CpuDetail => _snapshot is null ? "İşlemci kontrol ediliyor…" : $"{_snapshot.LogicalProcessors} mantıksal işlemci · anlık kullanım";
    public string ComputerText => _snapshot is null ? "Yerel bilgisayar" : $"{_snapshot.Computer} · {_snapshot.Windows}";
    public string UserMode => _userMode;

    private static string GetUserMode()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "Yönetici modu" : "Standart kullanıcı";
    }

    public async Task InitializeAsync()
    {
        await RunBusy("Bilgisayar bilgileri ve mevcut ayarlar okunuyor…", false, async _ =>
        {
            await RefreshMachineAsync(force: true);
            await ReadSettingsAndBackupsAsync();
            ReadHistory();
            Status = "Hazır · Tarama ve ayar değişiklikleri yalnızca sizin isteğinizle başlar.";
        });
    }

    public async Task RefreshMachineAsync(bool force = false)
    {
        if (_refreshing || (IsBusy && !force)) return;
        _refreshing = true;
        try
        {
            _snapshot = await Task.Run(_machine.Read);
            DriveCards.Clear();
            foreach (var drive in _snapshot.Drives) DriveCards.Add(new(drive));
            if (Drives.Count == 0)
            {
                foreach (var drive in _snapshot.Drives) Drives.Add(drive);
                SelectedDrive = Drives.FirstOrDefault();
            }
            NotifyMany(nameof(DiskFree), nameof(DiskDetail), nameof(DiskUsedPercent), nameof(MemoryPercent), nameof(MemoryUsedPercent),
                nameof(MemoryDetail), nameof(CpuPercent), nameof(CpuText), nameof(CpuDetail), nameof(ComputerText));
        }
        finally { _refreshing = false; }
    }

    private async Task ScanCleanupAsync()
    {
        Page = "Cleanup";
        _cleanupScan = null;
        foreach (var item in CleanupItems) item.Update(null);
        SelectionChanged();
        await RunBusy("Güvenli temizlik alanları taranıyor…", true, async token =>
        {
            var scan = await Task.Run(() => _cleanup.Scan(token, Progress("Taranıyor")));
            foreach (var item in CleanupItems) item.Update(scan.Groups.Single(group => group.Rule.Id == item.Rule.Id));
            _cleanupScan = scan.Cancelled ? null : scan;
            var bytes = scan.Groups.Sum(group => group.Bytes);
            CleanupHeadline = scan.Cancelled ? "Tarama durduruldu." : $"{Formatting.Bytes(bytes)} incelenebilir dosya";
            CleanupDetail = $"{Formatting.Count(scan.ExaminedFiles)} dosya incelendi · {Formatting.Count(scan.Skipped)} öğe atlandı · {scan.Duration.TotalSeconds:N1} saniye";
            if (scan.Cancelled) CleanupDetail += " · Temizlemek için taramayı tamamlayın.";
            Status = scan.Cancelled ? "Tarama durduruldu. Hiçbir dosya silinmedi." : "Tarama tamamlandı. Kategorileri seçip silinecek dosyaları önizleyebilirsiniz.";
            SelectionChanged();
        });
    }

    private async Task CleanAsync()
    {
        var scan = _cleanupScan;
        if (scan is null) return;
        var ids = CleanupItems.Where(item => item.Selected).Select(item => item.Rule.Id).ToHashSet(StringComparer.Ordinal);
        var files = scan.Groups.Where(group => ids.Contains(group.Rule.Id)).SelectMany(group => group.Files).ToArray();
        if (!_interaction.Confirm(new("Temizliği gözden geçir", $"{Formatting.Count(files.Length)} dosya · {Formatting.Bytes(files.Sum(file => file.Size))} mantıksal boyut",
            "Bu dosyalar KALICI olarak silinecek; Geri Dönüşüm Kutusu'na taşınmayacak. Yeni, kilitli veya taramadan sonra değişen dosyalar atlanır. Boş klasörler silinmez.",
            "Kalıcı temizle", "Listelenen dosyaların kalıcı silinmesini onaylıyorum.",
            files.Take(500).Select(file => new ReviewRow(Path.GetFileName(file.Path), file.Path, Formatting.Bytes(file.Size))).ToArray(),
            true, files.Length))) return;

        await RunBusy("Onaylanan dosyalar temizleniyor…", true, async token =>
        {
            var result = await Task.Run(() => _cleanup.Clean(scan, ids, token, Progress("Temizleniyor")));
            _cleanupScan = null;
            foreach (var item in CleanupItems) item.Update(null);
            CleanupHeadline = $"{Formatting.Bytes(result.DeletedBytes)} boyutunda dosya temizlendi";
            CleanupDetail = $"{Formatting.Count(result.Deleted)} dosya silindi · {Formatting.Count(result.Skipped)} dosya korundu/atlandı. Yeni işlem için tekrar tarayın.";
            Status = (result.Cancelled ? "Temizlik durduruldu. " : "Temizlik tamamlandı. ") + CleanupDetail;
            Record("Disk temizliği", CleanupDetail, result.Cancelled ? "Durduruldu" : result.Skipped > 0 ? "Kısmi" : "Tamamlandı", result.DeletedBytes);
            await RefreshMachineAsync(force: true);
            SelectionChanged();
            ShowIssues(result.Issues);
        });
    }

    private async Task ScanLargeAsync()
    {
        var root = ScanPath;
        var minimum = MinimumSize.Bytes;
        _largeScan = null;
        LargeFiles.Clear();
        Search = "";
        LargeDetail = "Tarama sürüyor…";
        NotifyLarge();
        await RunBusy("Büyük dosyalar aranıyor…", true, async token =>
        {
            var result = await Task.Run(() =>
            {
                var scan = DiskScanner.Scan(root, minimum, 500, token, Progress("Disk taranıyor"));
                var captured = scan.Files.Select(file => RecycleService.Capture(file, scan.Root)).ToArray();
                return (Scan: scan, Files: captured);
            });
            _largeScan = result.Scan;
            foreach (var captured in result.Files)
            {
                var item = new LargeFileItem(captured);
                item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(LargeFileItem.Selected)) SelectionChanged(); };
                LargeFiles.Add(item);
            }
            LargeDetail = $"{(result.Scan.Cancelled ? "Kısmi sonuç" : "Tarama tamamlandı")} · {result.Scan.Duration.TotalSeconds:N1} saniye · {result.Scan.Root}";
            Status = result.Scan.Cancelled ? "Tarama durduruldu. O ana kadar bulunan en büyük dosyalar gösteriliyor." : "En büyük dosyalar listelendi. Boyut, bir dosyanın gereksiz olduğunu göstermez.";
            NotifyLarge();
        });
    }

    private async Task RecycleAsync()
    {
        if (_largeScan is null) return;
        var root = _largeScan.Root;
        var selected = LargeFiles.Where(file => file.Selected && file.CanRecycle).ToArray();
        if (!_interaction.Confirm(new("Seçili dosyaları gözden geçir", $"{selected.Length} dosya · {Formatting.Bytes(selected.Sum(file => file.Bytes))}",
            "Seçili dosyalar Windows Geri Dönüşüm Kutusu'na taşınacak. Dosyalar uygulamalar için gerekli olabilir. Kutuyu kendiniz boşaltana kadar diskte boş alan artmaz. Kalıcı silme uygulanmaz.",
            "Geri Dönüşüm Kutusu'na taşı", "Bu dosyalara ihtiyacım olmadığını kontrol ettim.",
            selected.Select(file => new ReviewRow(file.Name, file.Path, file.Size)).ToArray()))) return;

        await RunBusy("Seçili dosyalar Geri Dönüşüm Kutusu'na taşınıyor…", true, async token =>
        {
            var result = await RecycleService.RecycleAsync(selected.Select(file => file.Captured).ToArray(), root, _interaction.WindowHandle, token);
            foreach (var file in selected)
            {
                if (!File.Exists(file.Path)) LargeFiles.Remove(file);
                else file.Selected = false;
            }
            Status = $"{result.Recycled} dosya Geri Dönüşüm Kutusu'na taşındı. Kutuyu boşaltana kadar diskte boş alan artmaz.";
            Record("Geri Dönüşüm Kutusu'na taşıma", Status, result.Cancelled ? "Durduruldu" : result.Issues.Count > 0 ? "Kısmi" : "Tamamlandı", result.Bytes);
            NotifyLarge();
            ShowIssues(result.Issues);
        });
    }

    private async Task ApplyTweaksAsync()
    {
        var selected = Tweaks.Where(tweak => tweak.Selected && tweak.CanChange).ToArray();
        if (!_interaction.Confirm(new("Performans ayarlarını gözden geçir", $"{selected.Length} ayar · Değişiklikten önce otomatik yerel yedek",
            "Yalnızca seçili ayarlar değişecek. Hız artışı garanti edilmez. Güç planı pil tüketimini artırabilir; oyun kaydını kapatmak kayıt özelliklerini etkiler. Yeniden başlatma otomatik yapılmaz.",
            "Yedekle ve uygula", "Seçili ayarların etkilerini okudum ve uygulanmasını onaylıyorum.",
            selected.Select(tweak => new ReviewRow(tweak.Title, tweak.Warning)).ToArray()))) return;

        await RunBusy("Önceki ayarlar yedekleniyor ve seçili değişiklikler uygulanıyor…", false, async _ =>
        {
            var result = await Task.Run(() => _settings.Apply(selected.Select(tweak => tweak.Definition.Id).ToHashSet(StringComparer.Ordinal)));
            await ReadSettingsAndBackupsAsync();
            Status = result.Message;
            if (!result.NoChanges) Record("Performans ayarları", result.Message, result.Success ? "Uygulandı" : "Hata");
            if (result.Issues.Count > 0) _interaction.Inform("Ayar işlemi ayrıntıları", string.Join(Environment.NewLine, result.Issues));
        });
    }

    private async Task RestoreAsync()
    {
        var selected = SelectedBackup;
        if (selected is null) return;
        if (!_interaction.Confirm(new("Önceki ayarlara dön", selected.Display,
            "Yalnızca bu yedeğin kapsadığı ayarlar geri yüklenir. Başka bir programda sonradan değiştirdiğiniz değerlerin üzerine yazılmaz. Bu işlem silinen dosyaları geri getirmez.",
            "Ayarları geri yükle", "Bu yedekteki önceki ayarların geri yüklenmesini onaylıyorum.",
            [new("Ayar yedeği", selected.Detail)]))) return;
        await RunBusy("Önceki ayarlar geri yükleniyor…", false, async _ =>
        {
            var result = await Task.Run(() => _settings.Restore(selected.Backup.Id));
            await ReadSettingsAndBackupsAsync();
            Status = result.Message;
            Record("Ayarları geri yükleme", result.Message, result.Success ? "Geri alındı" : "Kısmi");
            if (result.Issues.Count > 0) _interaction.Inform("Geri alma ayrıntıları", string.Join(Environment.NewLine, result.Issues));
        });
    }

    private Task RefreshSettingsAsync() => RunBusy("Mevcut ayarlar ve yedekler okunuyor…", false, async _ =>
    {
        await ReadSettingsAndBackupsAsync();
        Status = "Mevcut ayarlar yenilendi. Hiçbir ayar değiştirilmedi.";
    });

    private async Task ReadSettingsAndBackupsAsync()
    {
        var data = await Task.Run(() => (Statuses: _settings.ReadStatuses(), Backups: _backups.List()));
        foreach (var tweak in Tweaks) tweak.Update(data.Statuses.Single(status => status.Id == tweak.Definition.Id));
        var previousId = SelectedBackup?.Backup.Id;
        Backups.Clear();
        foreach (var backup in data.Backups) Backups.Add(new(backup));
        SelectedBackup = Backups.FirstOrDefault(item => item.Backup.Id == previousId) ?? Backups.FirstOrDefault(item => item.CanRestore) ?? Backups.FirstOrDefault();
        if (_backups.LastWarning is not null) _interaction.Inform("Yedek uyarısı", _backups.LastWarning);
        SelectionChanged();
    }

    private Task RefreshHistoryAsync() => RunBusy("İşlem geçmişi okunuyor…", false, _ =>
    {
        ReadHistory();
        Status = "Yerel işlem geçmişi yenilendi.";
        return Task.CompletedTask;
    });

    private void ReadHistory()
    {
        var entries = _history.Load();
        History.Clear();
        foreach (var entry in entries) History.Add(new(entry));
        Notify(nameof(HistoryEmpty));
    }

    private void Record(string title, string detail, string status, long bytes = 0)
    {
        try
        {
            _history.Add(new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, title, detail, status, bytes));
            ReadHistory();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status += " · Geçmiş kaydedilemedi; mevcut veri dosyası korundu.";
        }
    }

    private Task ExportLargeAsync()
    {
        var path = _interaction.PickCsvPath("ferah-buyuk-dosyalar.csv");
        if (path is null) return Task.CompletedTask;
        var rows = LargeFilesView.Cast<LargeFileItem>().Select(file => (IReadOnlyList<string>)
            new[] { file.Name, file.Path, file.Kind, file.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture), file.Status }).ToList();
        rows.Insert(0, ["Dosya", "Tam yol", "Tür", "Boyut (bayt)", "Durum"]);
        return RunBusy("Dosya listesi dışa aktarılıyor…", false, async token =>
        {
            await Task.Run(() => CsvExporter.Write(path, rows), token);
            Status = $"{rows.Count - 1} sonuç CSV olarak kaydedildi: {path}";
        });
    }

    private Task ExportHistoryAsync()
    {
        var path = _interaction.PickCsvPath("ferah-islem-gecmisi.csv");
        if (path is null) return Task.CompletedTask;
        var rows = History.Select(item => (IReadOnlyList<string>)new[] { item.Date, item.Title, item.Detail, item.Status, item.Size }).ToList();
        rows.Insert(0, ["Tarih", "İşlem", "Ayrıntı", "Durum", "Dosya boyutu"]);
        return RunBusy("İşlem geçmişi dışa aktarılıyor…", false, async token =>
        {
            await Task.Run(() => CsvExporter.Write(path, rows), token);
            Status = $"İşlem geçmişi CSV olarak kaydedildi: {path}";
        });
    }

    private void PickFolder()
    {
        try
        {
            var folder = _interaction.PickFolder();
            if (folder is null) return;
            if (!PathSafety.IsLocal(folder) || PathSafety.HasUnsafeAncestor(folder))
                throw new IOException("Bağlantı veya ağ yolu olmayan yerel bir klasör seçin.");
            SelectedDrive = null;
            ScanPath = folder;
            Status = "Klasör seçildi. Taramayı başlatmak için Tara düğmesine basın.";
        }
        catch (Exception error) { ReportError(error); }
    }

    private void OpenLocation(object? parameter)
    {
        if (parameter is not LargeFileItem file) return;
        try
        {
            if (!File.Exists(file.Path)) throw new FileNotFoundException("Dosya artık bu konumda değil; yeniden tarayın.");
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            _ = Process.Start(new ProcessStartInfo(explorer, $"/select,\"{file.Path}\"") { UseShellExecute = true });
        }
        catch (Exception error) { ReportError(error); }
    }

    private void OpenTool(object? parameter)
    {
        try
        {
            var target = parameter?.ToString() switch
            {
                "Startup" => "ms-settings:startupapps", "Storage" => "ms-settings:storagesense",
                "TaskManager" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe"),
                "Optimize" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dfrgui.exe"),
                "DiskCleanup" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cleanmgr.exe"),
                "RecycleBin" => "shell:RecycleBinFolder", "Data" => _dataFolder,
                _ => throw new ArgumentException("Bilinmeyen Windows aracı.")
            };
            if (target == _dataFolder) Directory.CreateDirectory(_dataFolder);
            _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error) { ReportError(error); }
    }

    private Progress<ScanProgress> Progress(string operation) => new(progress =>
    {
        if (!IsBusy || _cancellation?.IsCancellationRequested == true) return;
        Status = $"{operation} · {Formatting.Count(progress.Files)} dosya · {Formatting.Bytes(progress.Bytes)} · {Formatting.Count(progress.Skipped)} atlandı";
    });

    private async Task RunBusy(string status, bool cancellable, Func<CancellationToken, Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        CanCancel = cancellable;
        Status = status;
        _cancellation = new();
        try { await operation(_cancellation.Token); }
        catch (OperationCanceledException) { Status = "İşlem durduruldu."; }
        catch (Exception error) { ReportError(error); }
        finally
        {
            CanCancel = false;
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private void SelectionChanged()
    {
        NotifyMany(nameof(SelectedCleanupSize), nameof(SelectedCleanupCount), nameof(SelectedCleanupText),
            nameof(SelectedLargeCount), nameof(SelectedLargeText), nameof(SelectedTweakCount), nameof(SelectedTweakText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void NotifyLarge()
    {
        NotifyMany(nameof(LargeEmpty), nameof(VisibleLargeCount), nameof(ExaminedFilesText), nameof(ExaminedBytesText), nameof(SkippedText));
        SelectionChanged();
    }

    private void ShowIssues(IReadOnlyList<OperationIssue> issues)
    {
        if (issues.Count == 0) return;
        _interaction.Inform("Korunan veya atlanan dosyalar", string.Join("\n\n", issues.Take(10)
            .Select(issue => $"{issue.Path}\n{issue.Reason}")) + (issues.Count > 10 ? "\n\nİlk 10 ayrıntı gösteriliyor." : ""));
    }

    public void ReportError(Exception error)
    {
        Status = "İşlem tamamlanamadı: " + error.Message;
        _interaction.Inform("Ferah · İşlem tamamlanamadı", error.Message);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        GC.SuppressFinalize(this);
    }
}
