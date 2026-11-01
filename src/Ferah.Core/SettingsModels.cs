using Microsoft.Win32;

namespace Ferah.Core;

public sealed record RegistryDatum(bool Exists, RegistryValueKind Kind, string? Value)
{
    public static RegistryDatum Missing => new(false, RegistryValueKind.Unknown, null);
    public static RegistryDatum Dword(int value) => new(true, RegistryValueKind.DWord, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static RegistryDatum Text(string value) => new(true, RegistryValueKind.String, value);
}

public sealed record RegistryEdit(string Path, string Name, RegistryDatum Desired);

public sealed record TweakDefinition(
    string Id, string Title, string Description, string Warning, string Icon,
    string Badge, IReadOnlyList<RegistryEdit> Edits, Guid? PowerPlan = null, bool Recommended = false);

public sealed record TweakStatus(string Id, bool Applied, bool Available, string Detail);

public sealed record RegistryBackup(string Path, string Name, RegistryDatum Original, RegistryDatum Applied);

public sealed record TweakBackup(
    int Version, string Id, DateTime CreatedUtc, IReadOnlyList<string> TweakIds,
    IReadOnlyList<RegistryBackup> Registry, Guid? OriginalPower, Guid? AppliedPower, string Status);

public sealed record SettingsResult(bool Success, bool NoChanges, string Message, TweakBackup? Backup, IReadOnlyList<string> Issues);

public interface ISettingsBackend
{
    RegistryDatum Read(string path, string name);
    void Write(string path, string name, RegistryDatum value);
    Guid GetActivePowerPlan();
    IReadOnlyList<Guid> GetPowerPlans();
    void SetPowerPlan(Guid plan);
}

public interface IBackupStore
{
    void Save(TweakBackup backup);
    TweakBackup Load(string id);
    IReadOnlyList<TweakBackup> List();
}
