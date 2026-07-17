namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Lettura/scrittura del file wallet su disco (blueprint §8): JSON in chiaro
/// senza password, contenitore AES-GCM con password. Scrittura atomica
/// (file temporaneo + rename) per non corrompere il wallet su crash.
/// </summary>
public static class WalletStore
{
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>True se il file richiede una password per l'apertura.</summary>
    public static bool RequiresPassword(string path) =>
        EncryptedFile.IsEncrypted(File.ReadAllText(path));

    public static WalletDocument Load(string path, string? password = null)
    {
        var content = File.ReadAllText(path);
        if (EncryptedFile.IsEncrypted(content))
        {
            if (string.IsNullOrEmpty(password))
                throw new WrongPasswordException();
            content = EncryptedFile.Decrypt(content, password);
        }
        return WalletDocument.FromJson(content);
    }

    /// <param name="password">Null saves in plaintext. Only omit when the user has
    /// explicitly opted out of encryption (UI must show a clear warning).</param>
    public static void Save(WalletDocument doc, string path, string? password = null)
    {
        var content = doc.ToJson();
        if (!string.IsNullOrEmpty(password))
            content = EncryptedFile.Encrypt(content, password);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Same as <see cref="Save"/> but with the JSON serialization and disk write off the
    /// calling thread — for callers on a UI thread saving a large cache (thousands of cached
    /// transactions/headers), where the synchronous version would block the UI.
    /// </summary>
    public static Task SaveAsync(WalletDocument doc, string path, string? password = null) =>
        Task.Run(() => Save(doc, path, password));
}
