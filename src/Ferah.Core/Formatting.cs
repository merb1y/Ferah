using System.Globalization;

namespace Ferah.Core;

public static class Formatting
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Bytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = Math.Max(0, value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size.ToString(unit == 0 ? "N0" : "N1", Turkish)} {units[unit]}";
    }

    public static string Count(long value) => value.ToString("N0", Turkish);
    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("dd MMM yyyy · HH:mm", Turkish);
}
