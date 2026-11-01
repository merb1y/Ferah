namespace Ferah.Core;

public static class PathSafety
{
    private const FileAttributes CloudFlags = (FileAttributes)0x00440000;

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsInside(string path, string root)
    {
        var fullPath = Normalize(path);
        var fullRoot = Normalize(root);
        var prefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLocal(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.Length >= 3 && char.IsAsciiLetter(fullPath[0]) &&
               fullPath[1] == ':' && fullPath[2] == '\\' && !fullPath.AsSpan(2).Contains(':');
    }

    public static bool IsLinkOrCloud(FileAttributes attributes) =>
        (attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | CloudFlags)) != 0;

    public static bool HasUnsafeAncestor(string path)
    {
        var current = Normalize(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (IsLinkOrCloud(File.GetAttributes(current))) return true;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    public static string? RecycleBlockReason(DiskFile file, string scanRoot)
    {
        try
        {
            if (!IsLocal(file.Path) || !IsInside(file.Path, scanRoot))
                return "Yerel tarama kapsamı dışında.";
            if ((file.Attributes & (FileAttributes.System | FileAttributes.Directory)) != 0 ||
                IsLinkOrCloud(file.Attributes))
                return "Sistem dosyası, bağlantı veya bulut yer tutucusu.";

            var diskRoot = Path.GetPathRoot(file.Path)!;
            string[] protectedRoots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerahDesktop"),
                Path.Combine(diskRoot, "Windows"),
                Path.Combine(diskRoot, "Program Files"),
                Path.Combine(diskRoot, "Program Files (x86)"),
                Path.Combine(diskRoot, "ProgramData"),
                Path.Combine(diskRoot, "$Recycle.Bin"),
                Path.Combine(diskRoot, "System Volume Information")
            ];

            if (protectedRoots.Any(root => !string.IsNullOrWhiteSpace(root) &&
                (IsInside(file.Path, root) || string.Equals(Normalize(file.Path), Normalize(root), StringComparison.OrdinalIgnoreCase))))
                return "Windows veya uygulama klasörü korunuyor.";

            if (string.Equals(Path.GetDirectoryName(file.Path), diskRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(file.Path), ".sys", StringComparison.OrdinalIgnoreCase))
                return "Sürücü kökündeki dosyalar ve sistem sürücüleri korunuyor.";

            if (new DriveInfo(diskRoot).DriveType != DriveType.Fixed)
                return "Yalnızca yerel sabit disklerde Geri Dönüşüm Kutusu kullanılır.";

            return HasUnsafeAncestor(file.Path) ? "Bağlantı içeren dosya yolu korunuyor." : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Dosyaya güvenli biçimde erişilemiyor.";
        }
    }
}
