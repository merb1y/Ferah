using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ferah.Core;

public sealed class WindowsSettingsBackend : ISettingsBackend
{
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant);

    public RegistryDatum Read(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return RegistryDatum.Missing;
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var text = kind switch
        {
            RegistryValueKind.DWord => ((int)value!).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => ((long)value!).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value!),
            RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value!),
            RegistryValueKind.String or RegistryValueKind.ExpandString => (string)value!,
            _ => throw new IOException("Kayıt defteri değeri desteklenmeyen türde; değiştirilmeyecek.")
        };
        return new(true, kind, text);
    }

    public void Write(string path, string name, RegistryDatum value)
    {
        if (!value.Exists)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(path, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            existing?.Flush();
            return;
        }
        object data = value.Kind switch
        {
            RegistryValueKind.DWord => int.Parse(value.Value!, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(value.Value!, CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.FromBase64String(value.Value!),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Value!) ?? throw new InvalidDataException("Çoklu metin geçersiz."),
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.Value ?? "",
            _ => throw new InvalidDataException("Bu kayıt defteri türü desteklenmiyor.")
        };
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
            ?? throw new IOException("Kayıt defteri anahtarı açılamadı.");
        key.SetValue(name, data, value.Kind);
        key.Flush();
    }

    public Guid GetActivePowerPlan()
    {
        var match = GuidPattern.Match(RunPowercfg("/getactivescheme"));
        return match.Success ? Guid.Parse(match.Value) : throw new IOException("Etkin güç planı okunamadı.");
    }

    public IReadOnlyList<Guid> GetPowerPlans() => GuidPattern.Matches(RunPowercfg("/list"))
        .Select(match => Guid.Parse(match.Value)).Distinct().ToArray();

    public void SetPowerPlan(Guid plan) => RunPowercfg("/setactive", plan.ToString("D"));

    private static string RunPowercfg(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Windows güç aracı başlatılamadı.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: false);
            process.WaitForExit();
            throw new IOException("Windows güç aracı zaman aşımına uğradı.");
        }
        var text = output.GetAwaiter().GetResult();
        _ = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException("Windows güç planı işlemini reddetti. Kurum ilkesi veya yetki kısıtlaması olabilir.");
        return text;
    }
}
