using System.Text.Json;

namespace Ferah.Core;

public static class JsonStorage
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        if (PathSafety.HasUnsafeAncestor(path)) throw new IOException("Veri dosyası bir bağlantı içeriyor.");
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Veri dosyası güvenli boyut sınırını aşıyor.");
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options)
                ?? throw new InvalidDataException("Veri dosyası boş veya geçersiz.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Veri dosyası okunamadı. Mevcut dosya korundu; üzerine yazılmadı.", error);
        }
    }

    public static void Write<T>(string path, T value)
    {
        var folder = Path.GetDirectoryName(path) ?? throw new ArgumentException("Bir klasör gerekli.", nameof(path));
        Directory.CreateDirectory(folder);
        if (PathSafety.HasUnsafeAncestor(folder) || (File.Exists(path) && PathSafety.HasUnsafeAncestor(path)))
            throw new IOException("Veri klasörü veya dosyası bir bağlantı içeriyor.");
        var temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class HistoryStore(string folder)
{
    private readonly string _path = Path.Combine(folder, "history.json");
    private readonly object _gate = new();

    public IReadOnlyList<HistoryEntry> Load()
    {
        lock (_gate)
            return (JsonStorage.Read<List<HistoryEntry>>(_path) ?? [])
                .OrderByDescending(entry => entry.TimestampUtc).Take(100).ToArray();
    }

    public void Add(HistoryEntry entry)
    {
        lock (_gate)
        {
            var items = Load().Prepend(entry).Take(100).ToArray();
            JsonStorage.Write(_path, items);
        }
    }
}
