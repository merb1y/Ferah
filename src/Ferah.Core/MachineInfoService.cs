using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ferah.Core;

public sealed class MachineInfoService
{
    private ulong _idle, _kernel, _user;

    public MachineSnapshot Read()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        _ = GlobalMemoryStatusEx(ref memory);
        var cpu = 0d;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = idle.Value; var k = kernel.Value; var u = user.Value;
            var elapsed = k - _kernel + u - _user;
            if (_kernel != 0 && elapsed > 0) cpu = Math.Clamp((elapsed - (i - _idle)) * 100d / elapsed, 0, 100);
            _idle = i; _kernel = k; _user = u;
        }
        var drives = new List<DriveSnapshot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    drives.Add(new(drive.Name, string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Yerel disk" : drive.VolumeLabel,
                        drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception error) when (FileWalker.IsAccessError(error)) { }
        }
        return new(GetWindowsName(), Environment.MachineName, Environment.ProcessorCount,
            memory.TotalPhysical, memory.AvailablePhysical, cpu, drives);
    }

    private static string GetWindowsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = key?.GetValue("CurrentBuildNumber")?.ToString();
            var version = key?.GetValue("DisplayVersion")?.ToString();
            var windows = int.TryParse(build, out var number) && number >= 22000 ? "Windows 11" : "Windows 10";
            return $"{windows} · {version}";
        }
        catch (System.Security.SecurityException) { return "Windows"; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeTime idle, out NativeTime kernel, out NativeTime user);
}
