# Güvenlik

## Tasarım ilkeleri

Ferah, bakım uygulamalarının yüksek etkili işlemler yaptığını kabul ederek şu varsayılanları kullanır:

- **Önce tara, sonra göster, en son onay al.**
- Standart kullanıcı yetkisiyle çalış; ayrıcalık yükseltmeyi otomatik isteme.
- Bilinmeyen veya doğrulanamayan öğeyi değiştirmek yerine atla.
- Ayar değişikliğinden önce kalıcı yedek oluştur ve yazılan değeri tekrar oku.
- Başka bir programın sonradan değiştirdiği değerin üzerine geri alma sırasında yazma.
- Dosya yolu kadar dosya kimliğini ve açık dosya tanıtıcısını da doğrula.

## Dosya işlemleri

Kalıcı temizlik yalnızca kod içinde tanımlı dar kapsamlı klasörlerde çalışır. Tarama sırasında adayların Windows birim/dosya kimliği, boyutu, oluşturulma zamanı ve son yazılma zamanı kaydedilir. Silme anında:

1. Dosyanın izin verilen kökün altında olduğu doğrulanır.
2. Yolun hiçbir bileşeninde yeniden ayrıştırma noktası/bulut yer tutucusu olmadığı kontrol edilir.
3. Dosya, yeniden ayrıştırma noktalarını takip etmeden açılır.
4. Açık tanıtıcının çözümlenen gerçek yolu ve kimliği taramayla karşılaştırılır.
5. Yeni, salt okunur, sistem, çoklu sabit bağlantılı veya değişmiş dosya atlanır.
6. Doğrulama ve silme aynı açık tanıtıcı üzerinden yapılır.

Büyük dosya sayfası kalıcı silme sağlamaz. Windows Shell `IFileOperation` ve `FOFX_RECYCLEONDELETE` kullanılır; işlem Geri Dönüşüm Kutusu kipinde değilse ilerleme sink'i öğeyi reddeder.

## Ayar işlemleri

Desteklenen kayıt defteri yolları sabit bir izin listesinde bulunur ve yalnızca `HKEY_CURRENT_USER` kapsamındadır. Geri alma dosyası değiştirilmiş olsa bile izin listesi dışında bir değer uygulanmaz. Güç planı işlemleri yalnızca bilinen GUID'lerle ve argüman listesi üzerinden `powercfg.exe` çağrısıyla yapılır.

## Ağ ve gizlilik

Uygulamada HTTP istemcisi, telemetri, güncelleme denetleyicisi veya uzaktan komut özelliği yoktur. Geçmiş dosyasına tek tek dosya yolları yazılmaz. CSV dışa aktarma yalnızca kullanıcının seçtiği konuma yapılır.

## Üretim dağıtımı

Bu depodaki yerel paket imzasızdır. Genel dağıtımdan önce:

1. Derlemeyi temiz bir CI ortamında tekrarlayın.
2. Testleri çalıştırın.
3. EXE veya tercihen MSIX paketini kuruluşunuza ait güvenilir kod imzalama sertifikasıyla imzalayın.
4. SHA-256 özetlerini sürüm notlarına ekleyin.
5. Windows Defender ve Smart App Control ile temiz VM üzerinde test edin.

## Güvenlik açığı bildirimi

Genel bir hata kaydı açmadan önce; yeniden üretim adımlarını, Windows sürümünü ve ilgili hata iletisini proje sahibine özel kanaldan iletin. Kişisel dosya yollarını ve yedek dosyalarını paylaşmadan önce hassas bilgileri kaldırın.
