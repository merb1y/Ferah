using System.Text.Json;
using Ferah.Core;
using Microsoft.Win32;

var tests = new (string Name, Action Run)[]
{
    ("Bayt biçimlendirme", TestFormatting),
    ("CSV formül koruması", TestCsvEscaping),
    ("Yol sınırı denetimi", TestPathBoundary),
    ("Büyük dosya sıralaması", TestDiskScanner),
    ("Yeni geçici dosyayı koruma", TestCleanupAge),
    ("Temizlik planını tek kullanma", TestCleanupPlanIsSingleUse),
    ("Ayar yedeği ve geri alma", TestSettingsRoundTrip),
    ("Ayar hatasında otomatik geri alma", TestSettingsRollback),
    ("Sonradan değişen ayarı koruma", TestSettingsConflict),
    ("Bozuk yedeği reddetme", TestBackupValidation),
    ("Atomik JSON okuma", TestJsonStorage)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"[GEÇTİ] {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"[HATA]  {test.Name}: {error.Message}");
    }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} test geçti.");
return failures == 0 ? 0 : 1;

static void TestFormatting()
{
    Equal("1,0 KB", Formatting.Bytes(1024));
    Equal("0 B", Formatting.Bytes(-10));
}

static void TestCsvEscaping()
{
    Equal("\"'=2+2\"", CsvExporter.Escape("=2+2"));
    Equal("\"normal\"", CsvExporter.Escape("normal"));
    Equal("\"a\"\"b\"", CsvExporter.Escape("a\"b"));
}

static void TestPathBoundary()
{
    var root = Path.Combine(Path.GetTempPath(), "ferah-boundary");
    True(PathSafety.IsInside(Path.Combine(root, "child", "file.tmp"), root));
    True(!PathSafety.IsInside(root + "-other\\file.tmp", root));
    True(PathSafety.IsLocal(root));
}

static void TestDiskScanner()
{
    WithFolder(folder =>
    {
        File.WriteAllBytes(Path.Combine(folder, "small.bin"), new byte[8]);
        File.WriteAllBytes(Path.Combine(folder, "large.bin"), new byte[64]);
        var result = DiskScanner.Scan(folder, 0, 1, CancellationToken.None);
        Equal(1, result.Files.Count);
        Equal("large.bin", result.Files[0].Name);
        Equal(2L, result.ExaminedFiles);
    });
}

static void TestCleanupAge()
{
    WithFolder(folder =>
    {
        var path = Path.Combine(folder, "new.tmp");
        File.WriteAllText(path, "koru");
        var service = ServiceFor(folder);
        var scan = service.Scan(CancellationToken.None);
        Equal(0, scan.Groups.Single().Files.Count);
        True(File.Exists(path));
    });
}

static void TestCleanupPlanIsSingleUse()
{
    WithFolder(folder =>
    {
        var path = Path.Combine(folder, "old.tmp");
        File.WriteAllText(path, "sil");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
        var service = ServiceFor(folder);
        var scan = service.Scan(CancellationToken.None);
        Equal(1, scan.Groups.Single().Files.Count);
        var result = service.Clean(scan, new HashSet<string>(["test"]), CancellationToken.None);
        Equal(1, result.Deleted);
        True(!File.Exists(path));
        Throws<InvalidOperationException>(() => service.Clean(scan, new HashSet<string>(["test"]), CancellationToken.None));
    });
}

static CleanupService ServiceFor(string folder) => new(
    [new CleanupRule("test", "Test", "Test", "", [folder], 1)], _ => false);

static void TestSettingsRoundTrip()
{
    var backend = new FakeBackend();
    var store = new MemoryBackupStore();
    backend.Store = store;
    backend.Values[(TweakCatalog.All[0].Edits[0].Path, TweakCatalog.All[0].Edits[0].Name)] = RegistryDatum.Dword(1);
    var service = new SettingsService(backend, store);
    var applied = service.Apply(new HashSet<string>(["transparency"]));
    True(applied.Success && store.SavedBeforeWrite && applied.Backup is not null);
    Equal(RegistryDatum.Dword(0), backend.Read(TweakCatalog.All[0].Edits[0].Path, TweakCatalog.All[0].Edits[0].Name));
    var restored = service.Restore(applied.Backup!.Id);
    True(restored.Success);
    Equal(RegistryDatum.Dword(1), backend.Read(TweakCatalog.All[0].Edits[0].Path, TweakCatalog.All[0].Edits[0].Name));
}

static void TestSettingsRollback()
{
    var backend = new FakeBackend { FailOnWriteNumber = 2 };
    var store = new MemoryBackupStore();
    var tweak = TweakCatalog.All.Single(item => item.Id == "animations");
    foreach (var edit in tweak.Edits) backend.Values[(edit.Path, edit.Name)] = RegistryDatum.Dword(7);
    var result = new SettingsService(backend, store).Apply(new HashSet<string>(["animations"]));
    True(!result.Success);
    foreach (var edit in tweak.Edits) Equal(RegistryDatum.Dword(7), backend.Read(edit.Path, edit.Name));
}

static void TestSettingsConflict()
{
    var backend = new FakeBackend();
    var store = new MemoryBackupStore();
    var edit = TweakCatalog.All[0].Edits[0];
    backend.Values[(edit.Path, edit.Name)] = RegistryDatum.Dword(1);
    var service = new SettingsService(backend, store);
    var result = service.Apply(new HashSet<string>(["transparency"]));
    backend.Values[(edit.Path, edit.Name)] = RegistryDatum.Dword(99);
    var restored = service.Restore(result.Backup!.Id);
    True(!restored.Success && restored.Issues.Count == 1);
    Equal(RegistryDatum.Dword(99), backend.Read(edit.Path, edit.Name));
}

static void TestBackupValidation()
{
    var backend = new FakeBackend();
    var store = new MemoryBackupStore();
    var invalid = new TweakBackup(1, Guid.NewGuid().ToString("N"), DateTime.UtcNow, ["transparency"],
        [new RegistryBackup(@"Software\\Unexpected", "Value", RegistryDatum.Missing, RegistryDatum.Dword(1))], null, null, "Uygulandı");
    store.Save(invalid);
    Throws<InvalidDataException>(() => new SettingsService(backend, store).Restore(invalid.Id));
}

static void TestJsonStorage()
{
    WithFolder(folder =>
    {
        var path = Path.Combine(folder, "state.json");
        string[] values = ["Türkçe", "değer"];
        JsonStorage.Write(path, values);
        Equal("Türkçe", JsonStorage.Read<string[]>(path)![0]);
        File.WriteAllText(path, "bozuk");
        Throws<InvalidDataException>(() => JsonStorage.Read<string[]>(path));
        Equal("bozuk", File.ReadAllText(path));
    });
}

static void WithFolder(Action<string> action)
{
    var folder = Path.Combine(Path.GetTempPath(), "FerahTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try { action(folder); }
    finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Beklenen koşul sağlanmadı.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Beklenen: {expected}; gerçekleşen: {actual}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"{typeof(T).Name} bekleniyordu.");
}

internal sealed class FakeBackend : ISettingsBackend
{
    public Dictionary<(string Path, string Name), RegistryDatum> Values { get; } = [];
    public Guid ActivePower { get; set; } = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public int FailOnWriteNumber { get; init; }
    public int WriteCount { get; private set; }
    public MemoryBackupStore? Store { get; set; }
    public RegistryDatum Read(string path, string name) => Values.GetValueOrDefault((path, name), RegistryDatum.Missing);
    public void Write(string path, string name, RegistryDatum value)
    {
        WriteCount++;
        if (Store is not null && Store.Items.Count == 0) throw new InvalidOperationException("Yedek yazılmadan ayar değiştirildi.");
        if (FailOnWriteNumber == WriteCount) throw new IOException("Tasarlanmış yazma hatası.");
        if (value.Exists) Values[(path, name)] = value; else Values.Remove((path, name));
    }
    public Guid GetActivePowerPlan() => ActivePower;
    public IReadOnlyList<Guid> GetPowerPlans() => [ActivePower, TweakCatalog.HighPerformance];
    public void SetPowerPlan(Guid plan) => ActivePower = plan;
}

internal sealed class MemoryBackupStore : IBackupStore
{
    public List<TweakBackup> Items { get; } = [];
    public bool SavedBeforeWrite { get; private set; }
    public void Save(TweakBackup backup)
    {
        SavedBeforeWrite = true;
        Items.RemoveAll(item => item.Id == backup.Id);
        Items.Add(backup);
    }
    public TweakBackup Load(string id) => Items.Single(item => item.Id == id);
    public IReadOnlyList<TweakBackup> List() => Items;
}
