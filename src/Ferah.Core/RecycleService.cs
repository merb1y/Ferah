using System.Runtime.InteropServices;

namespace Ferah.Core;

public sealed record CapturedDiskFile(DiskFile File, FileIdentity? Identity, string? BlockReason);
public sealed record RecycleResult(int Recycled, long Bytes, bool Cancelled, IReadOnlyList<OperationIssue> Issues);

public static class RecycleService
{
    public static Task<RecycleResult> RecycleAsync(IReadOnlyList<CapturedDiskFile> files, string root,
        IntPtr owner, CancellationToken token)
    {
        var completion = new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Execute(files, root, owner, token)); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Ferah · Geri Dönüşüm Kutusu" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static CapturedDiskFile Capture(DiskFile file, string root)
    {
        var reason = PathSafety.RecycleBlockReason(file, root);
        if (reason is not null) return new(file, null, reason);
        try { return new(file, WindowsFileAccess.ReadIdentity(file.Path), null); }
        catch (Exception error) when (FileWalker.IsAccessError(error)) { return new(file, null, "Dosya kimliği okunamadı."); }
    }

    private static RecycleResult Execute(IReadOnlyList<CapturedDiskFile> files, string root, IntPtr owner, CancellationToken token)
    {
        var issues = new List<OperationIssue>();
        var sinks = new List<RecycleSink>();
        var shellItems = new List<IShellItem>();
        var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!;
        var operation = (IFileOperation)(Activator.CreateInstance(type) ?? throw new IOException("Windows dosya işlemi başlatılamadı."));
        var aborted = false;
        try
        {
            // Kalıcı silmeye geçen bir PreDeleteItem çağrısı aşağıdaki sink tarafından reddedilir.
            const uint flags = 0x00080000 | 0x20000000 | 0x0040 | 0x4000 | 0x2000 | 0x1000 | 0x0400 | 0x00100000;
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
            Marshal.ThrowExceptionForHR(operation.SetOwnerWindow(owner));
            Marshal.ThrowExceptionForHR(operation.SetProgressMessage("Ferah · Geri Dönüşüm Kutusu'na taşıma"));
            foreach (var captured in files.DistinctBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (token.IsCancellationRequested) { aborted = true; break; }
                if (captured.BlockReason is not null || captured.Identity is null)
                {
                    issues.Add(new(captured.File.Path, captured.BlockReason ?? "Dosya kimliği bulunamadı."));
                    continue;
                }
                var reason = PathSafety.RecycleBlockReason(captured.File, root);
                if (reason is not null) { issues.Add(new(captured.File.Path, reason)); continue; }
                try
                {
                    WindowsFileAccess.VerifyUnchanged(captured.File, captured.Identity);
                    var itemId = typeof(IShellItem).GUID;
                    Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(captured.File.Path, IntPtr.Zero, ref itemId, out var item));
                    shellItems.Add(item);
                    var sink = new RecycleSink(captured, root, token);
                    sinks.Add(sink);
                    Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
                }
                catch (Exception error) when (FileWalker.IsAccessError(error) || error is COMException)
                {
                    issues.Add(new(captured.File.Path, "Dosya değişmiş, kilitli veya erişilemiyor; atlandı."));
                }
            }
            if (!aborted && sinks.Count > 0)
            {
                var result = operation.PerformOperations();
                _ = operation.GetAnyOperationsAborted(out var shellAborted);
                aborted = shellAborted || token.IsCancellationRequested;
                if (result < 0) issues.Add(new("Windows", "Geri Dönüşüm Kutusu işlemi tamamlanamadı; kalıcı silme denenmedi."));
            }
            foreach (var sink in sinks.Where(sink => !sink.Succeeded))
                issues.Add(new(sink.Captured.File.Path, sink.Reason ?? "Windows öğeyi taşımadı veya işlem durduruldu."));
            return new(sinks.Count(sink => sink.Succeeded), sinks.Where(sink => sink.Succeeded).Sum(sink => sink.Captured.File.Size),
                aborted || token.IsCancellationRequested, issues.Take(100).ToArray());
        }
        finally
        {
            foreach (var item in shellItems) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
            GC.KeepAlive(sinks);
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleSink(CapturedDiskFile captured, string root, CancellationToken token) : IFileOperationProgressSink
    {
        public CapturedDiskFile Captured { get; } = captured;
        public bool Succeeded { get; private set; }
        public string? Reason { get; private set; }
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            if (token.IsCancellationRequested) { Reason = "İşlem durduruldu."; return unchecked((int)0x80004004); }
            if ((flags & 0x80) == 0) { Reason = "Kalıcı silme engellendi. Bu dosya geri dönüştürülemiyor."; return unchecked((int)0x80004004); }
            try
            {
                Reason = PathSafety.RecycleBlockReason(Captured.File, root);
                if (Reason is not null) return unchecked((int)0x80004004);
                WindowsFileAccess.VerifyUnchanged(Captured.File, Captured.Identity!);
                return 0;
            }
            catch (Exception error) when (FileWalker.IsAccessError(error))
            {
                Reason = "Dosya sonradan değişmiş veya erişilemiyor.";
                return unchecked((int)0x80004004);
            }
        }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created)
        {
            Succeeded = result >= 0 && (flags & 0x80) != 0 && !File.Exists(Captured.File.Path);
            return 0;
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? created) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string? name) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? created) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string? name) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? created) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string name, string? templateName, uint attributes, int result, IShellItem? created) => 0;
        public int UpdateProgress(uint total, uint complete) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem { }

[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? created);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? created);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint attributes, int result, IShellItem? created);
    [PreserveSig] int UpdateProgress(uint total, uint complete);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOperationFlags(uint flags);
    [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    [PreserveSig] int SetProgressDialog(IntPtr dialog);
    [PreserveSig] int SetProperties(IntPtr properties);
    [PreserveSig] int SetOwnerWindow(IntPtr owner);
    [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
    [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
    [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
    [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
    [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
    [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
    [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
    [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
    [PreserveSig] int DeleteItems(IntPtr items);
    [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
