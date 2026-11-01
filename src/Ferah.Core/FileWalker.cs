namespace Ferah.Core;

public sealed class FileWalker
{
    public int Skipped { get; private set; }

    public IEnumerable<DiskFile> Enumerate(string root, CancellationToken cancellationToken)
    {
        if (!PathSafety.IsLocal(root)) throw new ArgumentException("Yerel bir sürücü veya klasör seçin.", nameof(root));
        if (!Directory.Exists(root)) yield break;
        if (PathSafety.HasUnsafeAncestor(root))
        {
            Skipped++;
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(PathSafety.Normalize(root));
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            IEnumerator<FileSystemInfo>? iterator = null;
            try
            {
                if (PathSafety.IsLinkOrCloud(File.GetAttributes(folder))) { Skipped++; continue; }
                iterator = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options).GetEnumerator();
            }
            catch (Exception error) when (IsAccessError(error)) { Skipped++; }
            if (iterator is null) continue;

            using (iterator)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemInfo entry;
                    try
                    {
                        if (!iterator.MoveNext()) break;
                        entry = iterator.Current;
                    }
                    catch (Exception error) when (IsAccessError(error)) { Skipped++; break; }

                    DiskFile? file = null;
                    try
                    {
                        if (PathSafety.IsLinkOrCloud(entry.Attributes)) { Skipped++; continue; }
                        if (entry is DirectoryInfo) pending.Push(entry.FullName);
                        else if (entry is FileInfo info)
                            file = new DiskFile(info.FullName, info.Length, info.LastWriteTimeUtc, info.Attributes, info.CreationTimeUtc);
                    }
                    catch (Exception error) when (IsAccessError(error)) { Skipped++; }
                    if (file is not null) yield return file;
                }
            }
        }
    }

    internal static bool IsAccessError(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
