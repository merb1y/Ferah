using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ferah.Core;

// Silme yol adına değil, taramada kimliği kaydedilmiş dosyanın açık tanıtıcısına uygulanır.
public static class WindowsFileAccess
{
    private const uint ReadAttributes = 0x80;
    private const uint DeleteAccess = 0x00010000;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;

    public static FileIdentity ReadIdentity(string path)
    {
        using var handle = Open(path, ReadAttributes, FileShare.ReadWrite | FileShare.Delete);
        var info = GetInfo(handle);
        if (PathSafety.IsLinkOrCloud((FileAttributes)info.Attributes))
            throw new IOException("Bağlantı veya bulut dosyası atlandı.");
        return Identity(info);
    }

    public static void DeleteVerified(FileCandidate candidate, DateTime oldestAllowedUtc)
    {
        if (!PathSafety.IsInside(candidate.Path, candidate.Root) ||
            !PathSafety.IsLocal(candidate.Path) || PathSafety.HasUnsafeAncestor(candidate.Path))
            throw new IOException("Dosya izin verilen güvenli klasörün dışında.");

        using var handle = Open(candidate.Path, ReadAttributes | DeleteAccess, FileShare.Read);
        var info = GetInfo(handle);
        Verify(info, candidate.Identity, candidate.Size, candidate.LastWriteUtc);

        if (GetLastWrite(info) > oldestAllowedUtc || GetCreation(info) > oldestAllowedUtc || GetCreation(info) != candidate.CreationUtc)
            throw new IOException("Dosya artık yaş sınırını karşılamıyor.");
        if (!PathSafety.IsInside(GetResolvedPath(handle), candidate.Root))
            throw new IOException("Dosyanın gerçek konumu temizlik kapsamı dışında.");
        if (info.NumberOfLinks > 1)
            throw new IOException("Birden fazla sabit bağlantısı olan dosya korunuyor.");

        var disposition = new FileDisposition { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<FileDisposition>()))
            throw NativeError();
    }

    public static void VerifyUnchanged(DiskFile file, FileIdentity expected)
    {
        if (PathSafety.HasUnsafeAncestor(file.Path))
            throw new IOException("Dosya yolu bağlantı içeriyor.");
        using var handle = Open(file.Path, ReadAttributes, FileShare.ReadWrite | FileShare.Delete);
        Verify(GetInfo(handle), expected, file.Size, file.LastWriteUtc);
        if (!string.Equals(PathSafety.Normalize(GetResolvedPath(handle)), PathSafety.Normalize(file.Path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Dosyanın gerçek konumu değişti.");
    }

    private static void Verify(ByHandleFileInformation info, FileIdentity expected, long size, DateTime lastWrite)
    {
        if (PathSafety.IsLinkOrCloud((FileAttributes)info.Attributes) ||
            (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly)) != 0)
            throw new IOException("Bağlantı, salt okunur veya sistem dosyası korunuyor.");
        if (Identity(info) != expected || GetSize(info) != size || GetLastWrite(info) != lastWrite)
            throw new IOException("Dosya taramadan sonra değişmiş; yeniden tarayın.");
    }

    private static SafeFileHandle Open(string path, uint access, FileShare share)
    {
        var handle = CreateFileW(path, access, (uint)share, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw NativeError();
    }

    private static ByHandleFileInformation GetInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw NativeError();
        return info;
    }

    private static FileIdentity Identity(ByHandleFileInformation info) =>
        new(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    private static long GetSize(ByHandleFileInformation info) =>
        checked((long)(((ulong)info.FileSizeHigh << 32) | info.FileSizeLow));
    private static DateTime GetLastWrite(ByHandleFileInformation info) =>
        DateTime.FromFileTimeUtc(((long)info.LastWriteTime.High << 32) | info.LastWriteTime.Low);
    private static DateTime GetCreation(ByHandleFileInformation info) =>
        DateTime.FromFileTimeUtc(((long)info.CreationTime.High << 32) | info.CreationTime.Low);

    private static string GetResolvedPath(SafeFileHandle handle)
    {
        var buffer = new char[32768];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length) throw NativeError();
        var path = new string(buffer, 0, checked((int)length));
        return path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private static IOException NativeError() =>
        new("Dosya kilitli, erişim reddedildi veya işlem desteklenmiyor.", new Win32Exception(Marshal.GetLastWin32Error()));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDisposition { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, uint size);
}
