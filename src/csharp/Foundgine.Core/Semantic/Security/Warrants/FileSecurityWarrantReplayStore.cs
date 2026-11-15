using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Durable, cross-process replay protection backed by a lock-protected append-only file.
/// Suitable when all application instances share the same filesystem. Cloud deployments
/// should prefer a shared transactional store implementing <see cref="ISecurityWarrantReplayStore"/>
/// (for example Redis/SQL) rather than relying on a local filesystem.
/// </summary>
/// <remarks>
/// Each line is <c>HEX</c> (legacy, no lifetime, kept forever) or <c>HEX&lt;TAB&gt;retainUntilUnixMilliseconds</c>.
/// Entries consumed with an expiry are dropped once expired; the file is compacted when enough of it has expired, so it
/// stays bounded by the warrants issued within one lifetime instead of growing without limit.
/// </remarks>
public sealed class FileSecurityWarrantReplayStore : ISecurityWarrantReplayStore
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(2);

    /// <summary>Compact once this many expired lines have accumulated, or when at least half the file has expired.</summary>
    private const int CompactMinimumExpired = 1024;

    private readonly string _path;
    private readonly string _lockPath;
    private readonly TimeProvider _time;

    public FileSecurityWarrantReplayStore(string path) : this(path, TimeProvider.System)
    {
    }

    public FileSecurityWarrantReplayStore(string path, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A replay store path is required.", nameof(path));

        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _path = Path.GetFullPath(path);
        _lockPath = _path + ".lock";

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");

        using var _ = File.Open(
            _path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
    }

    public bool TryConsume(string warrantId, string nonce) =>
        Consume(warrantId, nonce, long.MaxValue, hasExpiry: false);

    public bool TryConsume(string warrantId, string nonce, DateTimeOffset expiresAt) =>
        Consume(warrantId, nonce, MemorySecurityWarrantReplayStore.RetainUntil(expiresAt), hasExpiry: true);

    private bool Consume(string warrantId, string nonce, long retainUntil, bool hasExpiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var identity = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    warrantId + "\u001f" + nonce)));
        var newLine = hasExpiry
            ? identity + "\t" + retainUntil.ToString(CultureInfo.InvariantCulture)
            : identity;
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();

        using var lockHandle = AcquireLock();

        var kept = new List<string>();
        var expired = 0;
        foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
        {
            if (line.Length == 0)
                continue;

            var tab = line.IndexOf('\t');
            var lineIdentity = tab < 0 ? line : line[..tab];
            if (StringComparer.Ordinal.Equals(lineIdentity, identity))
                return false;

            if (tab >= 0 && IsExpired(line, tab, now))
            {
                expired++;
                continue;
            }

            kept.Add(line);
        }

        if (expired >= CompactMinimumExpired || (expired > 0 && expired >= kept.Count))
        {
            kept.Add(newLine);
            Rewrite(kept);
        }
        else
        {
            Append(newLine);
        }

        return true;
    }

    private static bool IsExpired(string line, int tab, long now) =>
        // An unreadable lifetime keeps the entry: never silently re-open a consumed warrant.
        long.TryParse(line.AsSpan(tab + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var until)
        && until <= now;

    private void Append(string line)
    {
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

        writer.WriteLine(line);
        writer.Flush();
        append.Flush(true);
    }

    /// <summary>Replaces the file via a temp file in the same directory, so a crash never leaves a partial file.</summary>
    private void Rewrite(IReadOnlyList<string> lines)
    {
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temp,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var line in lines)
                    writer.WriteLine(line);

                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
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
