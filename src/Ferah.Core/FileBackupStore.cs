namespace Ferah.Core;

public sealed class FileBackupStore(string folder) : IBackupStore
{
    private readonly string _folder = Path.Combine(folder, "Backups");
    public string? LastWarning { get; private set; }

    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var guid)) throw new InvalidDataException("Yedek kimliği geçersiz.");
        return Path.Combine(_folder, $"{guid:N}.json");
    }

    public void Save(TweakBackup backup) => JsonStorage.Write(FilePath(backup.Id), backup);

    public TweakBackup Load(string id)
    {
        var backup = JsonStorage.Read<TweakBackup>(FilePath(id)) ?? throw new FileNotFoundException("Ayar yedeği bulunamadı.");
        if (backup.Id != id) throw new InvalidDataException("Yedek kimliği dosya adıyla eşleşmiyor.");
        return backup;
    }

    public IReadOnlyList<TweakBackup> List()
    {
        LastWarning = null;
        if (!Directory.Exists(_folder)) return [];
        if (PathSafety.HasUnsafeAncestor(_folder)) throw new IOException("Yedek klasörü bağlantı içeriyor.");
        var backups = new List<TweakBackup>();
        foreach (var file in Directory.EnumerateFiles(_folder, "*.json").Take(1000))
        {
            try { backups.Add(Load(Path.GetFileNameWithoutExtension(file))); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LastWarning = "Bazı yedekler okunamadı. Dosyalar korundu; üzerlerine yazılmadı.";
            }
        }
        return backups.OrderByDescending(backup => backup.CreatedUtc).Take(100).ToArray();
    }
}
