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

    public static void Save(WalletDocument doc, string path, string? password = null)
    {
        var content = doc.ToJson();
        if (!string.IsNullOrEmpty(password))
            content = EncryptedFile.Encrypt(content, password);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
