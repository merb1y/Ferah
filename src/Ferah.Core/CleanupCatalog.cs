namespace Ferah.Core;

public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupRule> Create()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return
        [
            new("user-temp", "Geçici dosyalar", "Uygulamaların bıraktığı, en az 7 gündür değişmeyen geçici dosyalar.", "\uE74D",
                [Path.GetTempPath()], 7),
            new("windows-temp", "Windows geçici dosyaları", "Windows Temp klasörü. Erişilemeyen ve kullanılan dosyalar atlanır.", "\uE770",
                [Path.Combine(windows, "Temp")], 7, RequiresAdmin: true),
            new("edge-cache", "Microsoft Edge önbelleği", "Yalnızca önbellek; parolalar, çerezler ve gezinme geçmişi korunur. Edge kapalı olmalı.", "\uE774",
                ChromiumRoots(Path.Combine(local, "Microsoft", "Edge", "User Data")), 7, false, BlockingProcess: "msedge"),
            new("chrome-cache", "Google Chrome önbelleği", "Yalnızca önbellek; parolalar, çerezler ve gezinme geçmişi korunur. Chrome kapalı olmalı.", "\uE774",
                ChromiumRoots(Path.Combine(local, "Google", "Chrome", "User Data")), 7, false, BlockingProcess: "chrome"),
            new("firefox-cache", "Mozilla Firefox önbelleği", "Yerel web önbelleği. Firefox kapalı olmalı; profil verileri temizlenmez.", "\uE774",
                FirefoxRoots(Path.Combine(local, "Mozilla", "Firefox", "Profiles")), 7, false, BlockingProcess: "firefox"),
            new("shader-cache", "Grafik önbelleği", "DirectX gölgelendirici önbelleği. Oyunlar ilk açılışta yeniden derleyebilir.", "\uE7F4",
                [Path.Combine(local, "D3DSCache")], 7, false),
            new("error-reports", "Eski hata raporları", "En az 30 günlük yerel Windows hata raporları. Sorun giderme için gerekli olabilir.", "\uE9D9",
                [Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                 Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue")], 30, false)
        ];
    }

    private static List<string> ChromiumRoots(string userData)
    {
        var roots = new List<string>();
        foreach (var profile in SafeDirectories(userData))
        {
            var name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            roots.Add(Path.Combine(profile, "Cache", "Cache_Data"));
            roots.Add(Path.Combine(profile, "Code Cache"));
            roots.Add(Path.Combine(profile, "GPUCache"));
        }
        return roots;
    }

    private static string[] FirefoxRoots(string profiles) =>
        SafeDirectories(profiles).Select(profile => Path.Combine(profile, "cache2", "entries")).ToArray();

    private static string[] SafeDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) && !PathSafety.HasUnsafeAncestor(root)
                ? Directory.GetDirectories(root) : [];
        }
        catch (Exception error) when (FileWalker.IsAccessError(error)) { return []; }
    }
}
