# Ferah

[![Windows CI](https://github.com/merb1y/Ferah/actions/workflows/ci.yml/badge.svg)](https://github.com/merb1y/Ferah/actions/workflows/ci.yml)

**Ferah**, Windows 10/11 için Türkçe, yerel ve onay odaklı bir disk/sistem bakım uygulamasıdır.

Uygulama üç temel işi yapar:

1. Güvenli kapsamlarla gereksiz dosyaları tarar ve kullanıcı seçtikten sonra temizler.
2. Bir disk veya klasördeki en büyük dosyaları listeler; kullanıcı dosyaları yalnızca açık onayla **Windows Geri Dönüşüm Kutusu'na** taşıyabilir.
3. Etkisi açıklanan, sınırlı sayıda performans ayarını önce yedekleyip sonra uygular; aynı yedekten geri alabilir.

Ferah yönetici olarak başlamaz, reklam/telemetri içermez ve internet bağlantısı kullanmaz.

## Özellikler

Tüm uygulama pencereleri, listeler, kaydırma çubukları ve iletişim kutuları tutarlı koyu tema kullanır; uygulama içinde açık tema yüzeyi bulunmaz.

### Disk temizliği

- Kullanıcı ve Windows geçici dosyaları
- Edge, Chrome ve Firefox önbellekleri
- DirectX gölgelendirici önbelleği
- Eski yerel Windows hata raporları
- Yaş sınırı, çalışan tarayıcı kontrolü, bağlantı/bulut dosyası koruması
- Taramadan sonra değişen, kullanılan veya erişilemeyen dosyaları otomatik atlama
- Silmeden önce dosya listesi, toplam boyut ve açık kalıcı silme onayı

Temizlik motoru klasörleri, kişisel tarayıcı verilerini, çerezleri, kayıtlı parolaları veya indirme geçmişini silmez. Boş klasörler de korunur.

### Büyük dosyalar

- Sürücü veya klasör seçimi
- 1 MB–1 GB arasında alt boyut eşiği
- En büyük 500 sonucu düşük bellek kullanımıyla sıralama
- Ad/yol araması ve CSV dışa aktarma
- Windows, Program Files, ProgramData, uygulama veri klasörü, sistem dosyaları, bağlantılar ve bulut yer tutucuları için taşıma koruması
- Seçili dosyaları kalıcı silmek yerine Windows Geri Dönüşüm Kutusu'na gönderme

> Bir dosyanın büyük olması, gereksiz olduğu anlamına gelmez. Dosyayı seçmeden önce konumunu ve kullanan uygulamayı kontrol edin.

### Performans ayarları

- Saydamlığı kapatma
- Pencere/görev çubuğu animasyonlarını azaltma
- Arka plan oyun kaydını kapatma
- Cihazda zaten mevcutsa Yüksek Performans güç planına geçme

Ferah; Microsoft Defender'ı, Güvenlik Duvarı'nı, Windows Update'i, kritik hizmetleri veya kurtarma özelliklerini kapatmaz. Ayarlar yalnızca `HKEY_CURRENT_USER` altında yapılır; güç planında Windows'un kendi `powercfg.exe` aracı kullanılır.

Her değişiklikten **önce** eski değerler `%LOCALAPPDATA%\FerahDesktop\Backups` dizinine atomik JSON yedeği olarak yazılır. Yedek yazılamazsa hiçbir ayar değiştirilmez. İşlem yarıda kalırsa uygulanan değerler otomatik geri alınmaya çalışılır.

## Hazır uygulamayı çalıştırma

Dağıtım oluşturulduktan sonra:

```text
artifacts\Ferah-1.0.0-win-x64\Ferah.exe
```

dosyasını açın. Hazır `win-x64` sürümü kendi .NET çalışma zamanını içerir; ayrıca .NET kurmanız gerekmez.

Windows, internette yayımlanmamış ve kod imzası olmayan uygulamalarda SmartScreen uyarısı gösterebilir. Üretim dağıtımında EXE/MSIX paketinin güvenilir bir kod imzalama sertifikasıyla imzalanması önerilir.

## Kaynaktan geliştirme

Gereksinimler:

- Windows 10/11
- .NET 10 SDK

Derleme ve test:

```powershell
.\build.ps1
```

Tek dosyalı, kendi çalışma zamanını içeren x64 dağıtım:

```powershell
.\publish.ps1
```

Elle çalıştırma:

```powershell
dotnet run --project .\src\Ferah.App\Ferah.App.csproj
```

İsteğe bağlı başlangıç sayfası parametreleri:

```text
--cleanup
--large-files
--performance
--history
```

## Proje yapısı

```text
src/Ferah.App/       WPF arayüzü ve kullanıcı etkileşimi
src/Ferah.Core/      Tarama, güvenli silme, geri dönüşüm, ayar ve yedek motorları
tests/Ferah.Tests/   Harici test paketi gerektirmeyen güvenlik/regresyon testleri
```

## Güvenlik modeli

- Taramalar salt okunurdur.
- Temizlik planı tek kullanımlıdır ve 30 dakika sonra geçersiz olur.
- Dosya; boyut, oluşturulma/değiştirilme zamanı ve Windows dosya kimliğiyle yeniden doğrulanır.
- Bağlantı noktaları, sembolik bağlantılar, bulut yer tutucuları ve birden fazla sabit bağlantısı olan dosyalar temizlenmez.
- Büyük dosya eylemi, `IFileOperation` için `FOFX_RECYCLEONDELETE` kullanır; kalıcı silmeye geçişi reddeden ilerleme denetimi de vardır.
- CSV hücreleri, elektronik tablo formül enjeksiyonuna karşı metin olarak kaçışlanır.
- Bozuk veya izin verilmeyen ayar yedekleri kayıt defterine uygulanmaz.
- Uygulama komut kabuğundan keyfi kullanıcı metni çalıştırmaz.

Daha ayrıntılı bilgi: [SECURITY.md](SECURITY.md)

## Yerel veriler

Ferah yalnızca aşağıdaki konumda veri tutar:

```text
%LOCALAPPDATA%\FerahDesktop
```

- `history.json`: En fazla son 100 işlemin özeti; dosya yolları kaydedilmez.
- `Backups\*.json`: Geri alınabilir ayar yedekleri.

Uygulama bu verileri herhangi bir sunucuya göndermez.

## Sınırlamalar

- Büyük dosya taramasındaki boyutlar mantıksal dosya boyutudur; sıkıştırılmış/seyrek dosyanın diskte kapladığı fiziksel alan farklı olabilir.
- Erişim izni olmayan klasörler ve kullanılan dosyalar atlanır; bu normal ve güvenli davranıştır.
- Windows Temp alanının bir bölümü standart kullanıcı hesabıyla okunamayabilir. Ferah ayrıcalık yükseltmeyi otomatik istemez.
- Geri Dönüşüm Kutusu'na taşınan dosyalar, kutu boşaltılana kadar diskte yer kaplamaya devam eder.
- Güç planları donanım üreticisine ve Modern Standby desteğine göre değişir. Olmayan bir plan oluşturulmaz.

## İlham ve lisans

Ürün kapsamı ve güvenli onay yaklaşımı tasarlanırken açık kaynak [Chris Titus Tech WinUtil](https://github.com/ChrisTitusTech/winutil) projesi incelenmiştir. Ferah'ın kodu sıfırdan yazılmıştır; WinUtil kodu paketlenmez veya çalıştırılmaz. Ayrıntılar [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) dosyasındadır.

Ferah, [MIT Lisansı](LICENSE) ile sunulur.
