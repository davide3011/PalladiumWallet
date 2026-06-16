using System.Security.Cryptography;
using NBitcoin;

namespace PalladiumWallet.Core.Spv;

/// <summary>
/// Script hash for the indexing protocol (blueprint §0/§10): SHA-256 of the
/// scriptPubKey with bytes reversed, encoded as lowercase hex.
/// </summary>
public static class Scripthash
{
    public static string FromScript(Script scriptPubKey)
    {
        var hash = SHA256.HashData(scriptPubKey.ToBytes());
        Array.Reverse(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FromAddress(BitcoinAddress address) =>
        FromScript(address.ScriptPubKey);
}
