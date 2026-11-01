namespace Ferah.Core;

public static class TweakCatalog
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new("transparency", "Saydamlığı kapat", "Başlat menüsü ve görev çubuğundaki saydam efektleri azaltır.",
            "Görünüm değişir. Etkisi donanıma göre değişir; hız artışı garantisi yoktur. Oturumu yeniden açmanız gerekebilir.", "\uE790", "Düşük risk",
            [new(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryDatum.Dword(0))], Recommended: true),
        new("animations", "Pencere animasyonlarını azalt", "Pencere küçültme ve görev çubuğu animasyonlarını kapatır.",
            "Arayüz daha sade görünür. Değişiklikler oturum yeniden açıldığında tam olarak uygulanır.", "\uE7F8", "Düşük risk",
            [new(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryDatum.Text("0")),
             new(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", RegistryDatum.Dword(0))], Recommended: true),
        new("game-capture", "Arka plan oyun kaydını kapat", "Game DVR ve uygulama yakalamayı kapatarak kayıt yükünü azaltabilir.",
            "Xbox Game Bar ile oyun klibi ve ekran kaydı özellikleri etkilenir. Kayıt alıyorsanız seçmeyin.", "\uE714", "İşlev değişir",
            [new(@"System\GameConfigStore", "GameDVR_Enabled", RegistryDatum.Dword(0)),
             new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryDatum.Dword(0))]),
        new("power", "Yüksek performans güç planı", "Windows'ta zaten mevcutsa yüksek performans planına geçer.",
            "Pil tüketimi, sıcaklık ve fan sesi artabilir. Dizüstünde dengeli plan genellikle daha uygundur. Desteklenmeyen plan oluşturulmaz.", "\uE945", "Enerji tüketimi",
            [], HighPerformance)
    ];
}
