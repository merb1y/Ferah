using System.Text;

namespace Ferah.Core;

public static class CsvExporter
{
    public static string Escape(string value)
    {
        if (value.Length > 0 && ("=+-@\t\r\n".Contains(value[0]) ||
            "=+-@".Contains(value.TrimStart().FirstOrDefault()))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static void Write(string path, IEnumerable<IReadOnlyList<string>> rows)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        foreach (var row in rows) writer.WriteLine(string.Join(';', row.Select(Escape)));
    }
}
