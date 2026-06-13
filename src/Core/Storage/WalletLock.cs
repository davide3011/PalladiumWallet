namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Exclusive lock on a wallet file held for the lifetime of the session.
/// The real lock is the open FileStream with FileShare.None — the .lock file
/// is just its vessel. The OS releases it automatically on process exit or crash.
/// </summary>
public sealed class WalletLock : IDisposable
{
    private readonly string _lockPath;
    private FileStream? _stream;

    private WalletLock(string lockPath, FileStream stream)
    {
        _lockPath = lockPath;
        _stream = stream;
    }

    /// <summary>
    /// Tries to acquire an exclusive lock for <paramref name="walletPath"/>.
    /// Returns null if another process already holds the lock (IOException).
    /// Lets UnauthorizedAccessException propagate so callers can show a distinct message.
    /// </summary>
    public static WalletLock? TryAcquire(string walletPath)
    {
        var lockPath = walletPath + ".lock";
        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new WalletLock(lockPath, stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        try { File.Delete(_lockPath); } catch { }
    }
}
