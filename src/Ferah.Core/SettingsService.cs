namespace Ferah.Core;

public sealed class SettingsService(ISettingsBackend backend, IBackupStore store)
{
    private readonly object _gate = new();

    public IReadOnlyList<TweakStatus> ReadStatuses()
    {
        var statuses = new List<TweakStatus>();
        foreach (var tweak in TweakCatalog.All)
        {
            try
            {
                var available = tweak.PowerPlan is null || backend.GetPowerPlans().Contains(tweak.PowerPlan.Value);
                var applied = available && (tweak.PowerPlan is not null
                    ? backend.GetActivePowerPlan() == tweak.PowerPlan.Value
                    : tweak.Edits.All(edit => backend.Read(edit.Path, edit.Name) == edit.Desired));
                statuses.Add(new(tweak.Id, applied, available, !available ? "Bu cihazda bu güç planı yok." : applied ? "Zaten uygulanmış" : "Uygulanmadı"));
            }
            catch (Exception error) when (IsBackendError(error))
            {
                statuses.Add(new(tweak.Id, false, false, "Mevcut ayar okunamadı."));
            }
        }
        return statuses;
    }

    public SettingsResult Apply(IReadOnlySet<string> selected)
    {
        lock (_gate)
        {
            if (selected.Count == 0 || selected.Any(id => TweakCatalog.All.All(tweak => tweak.Id != id)))
                throw new ArgumentException("En az bir geçerli performans ayarı seçin.", nameof(selected));
            var tweaks = TweakCatalog.All.Where(tweak => selected.Contains(tweak.Id)).ToArray();
            var changes = tweaks.SelectMany(tweak => tweak.Edits)
                .Select(edit => new RegistryBackup(edit.Path, edit.Name, backend.Read(edit.Path, edit.Name), edit.Desired))
                .Where(change => change.Original != change.Applied).ToArray();
            var desiredPower = tweaks.Select(tweak => tweak.PowerPlan).FirstOrDefault(plan => plan is not null);
            Guid? originalPower = null;
            if (desiredPower is not null)
            {
                if (!backend.GetPowerPlans().Contains(desiredPower.Value))
                    throw new InvalidOperationException("Yüksek performans güç planı bu cihazda mevcut değil.");
                originalPower = backend.GetActivePowerPlan();
                if (originalPower == desiredPower) { desiredPower = null; originalPower = null; }
            }
            if (changes.Length == 0 && desiredPower is null)
                return new(true, true, "Seçili ayarlar zaten uygulanmış. Hiçbir değişiklik yapılmadı.", null, []);

            var backup = new TweakBackup(1, Guid.NewGuid().ToString("N"), DateTime.UtcNow,
                tweaks.Select(tweak => tweak.Id).ToArray(), changes, originalPower, desiredPower, "Hazır");
            // Yedek kalıcı diske yazılamazsa aşağıdaki değişikliklere hiç başlanmaz.
            store.Save(backup);
            try
            {
                foreach (var change in changes)
                {
                    if (backend.Read(change.Path, change.Name) != change.Original)
                        throw new InvalidOperationException("Bir ayar hazırlık sırasında başka bir program tarafından değiştirildi.");
                    backend.Write(change.Path, change.Name, change.Applied);
                    if (backend.Read(change.Path, change.Name) != change.Applied)
                        throw new IOException("Yazılan ayar doğrulanamadı.");
                }
                if (desiredPower is not null)
                {
                    if (backend.GetActivePowerPlan() != originalPower)
                        throw new InvalidOperationException("Güç planı hazırlık sırasında değiştirildi.");
                    backend.SetPowerPlan(desiredPower.Value);
                    if (backend.GetActivePowerPlan() != desiredPower) throw new IOException("Güç planı doğrulanamadı.");
                }
                backup = backup with { Status = "Uygulandı" };
                store.Save(backup);
                return new(true, false, "Ayarlar uygulandı ve önceki değerler yedeklendi. Oturumu yeniden açmanız gerekebilir.", backup, []);
            }
            catch (Exception error) when (IsBackendError(error))
            {
                var issues = RestoreValues(backup);
                backup = backup with { Status = issues.Count == 0 ? "Geri alındı" : "Kısmen geri alındı" };
                try { store.Save(backup); }
                catch (Exception saveError) when (IsBackendError(saveError)) { issues.Add("Yedek durumu güncellenemedi; ilk yedek korundu."); }
                return new(false, false, issues.Count == 0
                    ? "İşlem başarısız oldu; yapılan değişiklikler geri alındı. " + error.Message
                    : "İşlem başarısız oldu; bazı ayarlar geri alınamadı. Yedeği ve ayrıntıları inceleyin.", backup, issues);
            }
        }
    }

    public SettingsResult Restore(string id)
    {
        lock (_gate)
        {
            var backup = store.Load(id);
            ValidateBackup(backup);
            var issues = RestoreValues(backup);
            backup = backup with { Status = issues.Count == 0 ? "Geri alındı" : "Kısmen geri alındı" };
            try { store.Save(backup); }
            catch (Exception error) when (IsBackendError(error)) { issues.Add("Geri alma durumu diske yazılamadı."); }
            return new(issues.Count == 0, false, issues.Count == 0
                ? "Yedekteki önceki ayarlar geri yüklendi. Oturumu yeniden açmanız gerekebilir."
                : "Bazı ayarlar başka bir yerde değiştirildi veya erişilemedi; bu değerlerin üzerine yazılmadı.", backup, issues);
        }
    }

    private List<string> RestoreValues(TweakBackup backup)
    {
        var issues = new List<string>();
        foreach (var change in backup.Registry.Reverse())
        {
            try
            {
                var current = backend.Read(change.Path, change.Name);
                if (current == change.Original) continue;
                if (current != change.Applied)
                {
                    issues.Add($"{change.Name}: Sonradan değiştirilmiş; korunuyor.");
                    continue;
                }
                backend.Write(change.Path, change.Name, change.Original);
                if (backend.Read(change.Path, change.Name) != change.Original)
                    throw new IOException("Önceki değer doğrulanamadı.");
            }
            catch (Exception error) when (IsBackendError(error)) { issues.Add($"{change.Name}: Önceki değer geri yüklenemedi."); }
        }
        if (backup.OriginalPower is not null && backup.AppliedPower is not null)
        {
            try
            {
                var current = backend.GetActivePowerPlan();
                if (current == backup.AppliedPower)
                {
                    if (!backend.GetPowerPlans().Contains(backup.OriginalPower.Value))
                        throw new IOException("Önceki güç planı artık mevcut değil.");
                    backend.SetPowerPlan(backup.OriginalPower.Value);
                    if (backend.GetActivePowerPlan() != backup.OriginalPower) throw new IOException("Önceki güç planı doğrulanamadı.");
                }
                else if (current != backup.OriginalPower) issues.Add("Güç planı sonradan değiştirilmiş; korunuyor.");
            }
            catch (Exception error) when (IsBackendError(error)) { issues.Add("Önceki güç planı geri yüklenemedi."); }
        }
        return issues;
    }

    private static void ValidateBackup(TweakBackup backup)
    {
        if (backup.Version != 1 || backup.TweakIds is null || backup.Registry is null ||
            backup.Registry.Count > 20 || backup.TweakIds.Count == 0 ||
            backup.TweakIds.Any(id => TweakCatalog.All.All(tweak => tweak.Id != id)))
            throw new InvalidDataException("Yedek bilinen performans ayarlarıyla eşleşmiyor.");
        var allowed = TweakCatalog.All.Where(tweak => backup.TweakIds.Contains(tweak.Id))
            .SelectMany(tweak => tweak.Edits).ToArray();
        if (backup.Registry.Any(change => change.Original is null || change.Applied is null || !allowed.Any(edit =>
            edit.Path == change.Path && edit.Name == change.Name && edit.Desired == change.Applied)) ||
            backup.Registry.Select(change => change.Path + "\0" + change.Name).Distinct().Count() != backup.Registry.Count)
            throw new InvalidDataException("Yedekte izin verilmeyen kayıt defteri değerleri var.");
        if ((backup.AppliedPower is null) != (backup.OriginalPower is null) ||
            (backup.AppliedPower is not null && (!backup.TweakIds.Contains("power") || backup.AppliedPower != TweakCatalog.HighPerformance)))
            throw new InvalidDataException("Yedekte izin verilmeyen güç planı değişikliği var.");
    }

    private static bool IsBackendError(Exception error) => error is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or System.Security.SecurityException or System.ComponentModel.Win32Exception;
}
