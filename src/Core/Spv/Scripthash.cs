using System.Security.Cryptography;
using NBitcoin;

namespace PalladiumWallet.Core.Spv;

/// <summary>
/// Scripthash del protocollo di indicizzazione (blueprint §0/§10): SHA-256
/// dello scriptPubKey con i byte in ordine inverso, esadecimale.
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
