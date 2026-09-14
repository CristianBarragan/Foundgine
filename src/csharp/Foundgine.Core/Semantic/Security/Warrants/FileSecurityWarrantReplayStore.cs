using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Durable, cross-process replay protection backed by a lock-protected append-only file.
/// Suitable when all application instances share the same filesystem. Cloud deployments
/// should prefer a shared transactional store implementing <see cref="ISecurityWarrantReplayStore"/>
/// (for example Redis/SQL) rather than relying on a local filesystem.
/// </summary>
public sealed class FileSecurityWarrantReplayStore : ISecurityWarrantReplayStore
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(2);

    private readonly string _path;
    private readonly string _lockPath;

    public FileSecurityWarrantReplayStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A replay store path is required.", nameof(path));

        _path = Path.GetFullPath(path);
        _lockPath = _path + ".lock";

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");

        using var _ = File.Open(
            _path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
    }

    public bool TryConsume(string warrantId, string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var identity = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    warrantId + "\u001f" + nonce)));

        using var lockHandle = AcquireLock();

        foreach (var line in File.ReadLines(_path))
        {
            if (StringComparer.Ordinal.Equals(line, identity))
                return false;
        }

        using var append = new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough);

        using var writer = new StreamWriter(
            append,
            new UTF8Encoding(false));

        writer.WriteLine(identity);
        writer.Flush();
        append.Flush(true);

        return true;
    }

    private FileStream AcquireLock()
    {
        var started = DateTime.UtcNow;

        while (true)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough | FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow - started >= LockTimeout)
                {
                    TryRemoveStaleLock();

                    if (DateTime.UtcNow - started >= LockTimeout)
                    {
                        throw new TimeoutException(
                            $"Timed out acquiring the replay store lock '{_lockPath}'.");
                    }
                }

                Thread.Sleep(LockRetryDelay);
            }
        }
    }

    private void TryRemoveStaleLock()
    {
        try
        {
            if (!File.Exists(_lockPath))
                return;

            var lastWriteUtc = File.GetLastWriteTimeUtc(_lockPath);

            if (DateTime.UtcNow - lastWriteUtc < StaleLockAge)
                return;

            File.Delete(_lockPath);
        }
        catch (IOException)
        {
            // Another process may currently own or be removing the lock.
        }
        catch (UnauthorizedAccessException)
        {
            // The lock may have changed between inspection and deletion.
        }
    }
}