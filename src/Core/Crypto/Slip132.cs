using NBitcoin;
using NBitcoin.DataEncoders;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Serializzazione e parsing SLIP-132 delle chiavi estese (xprv/yprv/zprv e
/// varianti pubbliche) con gli header di <see cref="ChainProfile.ExtKeyHeaders"/>
/// (blueprint §3). NBitcoin serializza con i soli header registrati nella Network
/// (xprv/xpub): le altre varianti si compongono qui come
/// Base58Check( header 4 byte BE || payload BIP32 74 byte ).
/// </summary>
public static class Slip132
{
    public static string Encode(ExtPubKey key, ScriptKind kind, ChainProfile profile) =>
        Encoders.Base58Check.EncodeData([.. profile.ExtKeyHeaders[kind].PublicBytes(), .. key.ToBytes()]);

    public static string Encode(ExtKey key, ScriptKind kind, ChainProfile profile) =>
        Encoders.Base58Check.EncodeData([.. profile.ExtKeyHeaders[kind].PrivateBytes(), .. key.ToBytes()]);

    /// <summary>
    /// Recognises the header (→ ScriptKind) and decodes an extended public key:
    /// this is the watch-only import path (§4.4).
    /// </summary>
    public static bool TryDecodePublic(string encoded, ChainProfile profile,
        out ExtPubKey? key, out ScriptKind kind)
    {
        key = null;
        kind = default;
        if (!TryDecodePayload(encoded, profile, isPrivate: false, out var payload, out kind))
            return false;
        try
        {
            key = new ExtPubKey(payload!);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Come <see cref="TryDecodePublic"/>, per chiavi private (import spendibile).</summary>
    public static bool TryDecodePrivate(string encoded, ChainProfile profile,
        out ExtKey? key, out ScriptKind kind)
    {
        key = null;
        kind = default;
        if (!TryDecodePayload(encoded, profile, isPrivate: true, out var payload, out kind))
            return false;
        try
        {
            key = ExtKey.CreateFromBytes(payload!);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryDecodePayload(string encoded, ChainProfile profile, bool isPrivate,
        out byte[]? payload, out ScriptKind kind)
    {
        payload = null;
        kind = default;
        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        byte[] data;
        try
        {
            data = Encoders.Base58Check.DecodeData(encoded.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (data.Length != 78)
            return false;

        var header = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
        foreach (var (candidate, headers) in profile.ExtKeyHeaders)
        {
            if (header != (isPrivate ? headers.Private : headers.Public))
                continue;
            kind = candidate;
            payload = data[4..];
            return true;
        }

        return false;
    }
}
