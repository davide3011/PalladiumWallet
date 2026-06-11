using NBitcoin;
using NBitcoin.Crypto;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Spv;

/// <summary>
/// Header di blocco parsato dagli 80 byte canonici (blueprint §7.2):
/// versione, prev_hash, merkle_root, timestamp, bits, nonce.
/// </summary>
public sealed record BlockHeaderInfo(
    int Version, uint256 PrevHash, uint256 MerkleRoot, uint Timestamp, uint Bits, uint Nonce, uint256 Hash)
{
    public const int Size = 80;

    public static BlockHeaderInfo Parse(string headerHex) => Parse(Convert.FromHexString(headerHex));

    public static BlockHeaderInfo Parse(byte[] raw)
    {
        if (raw.Length != Size)
            throw new ArgumentException($"Header di {raw.Length} byte (attesi {Size}).", nameof(raw));

        return new BlockHeaderInfo(
            Version: BitConverter.ToInt32(raw, 0),
            PrevHash: new uint256(raw.AsSpan(4, 32)),
            MerkleRoot: new uint256(raw.AsSpan(36, 32)),
            Timestamp: BitConverter.ToUInt32(raw, 68),
            Bits: BitConverter.ToUInt32(raw, 72),
            Nonce: BitConverter.ToUInt32(raw, 76),
            Hash: new uint256(Hashes.DoubleSHA256RawBytes(raw, 0, Size)));
    }

    /// <summary>
    /// Validazione SPV dell'header (§7.2): collegamento al precedente e,
    /// se il profilo non impone lo skip (catena LWMA), verifica PoW
    /// hash &lt;= target dai bits. Il controllo di coerenza dei bits col
    /// retargeting completo richiede la storia: per la catena di riferimento
    /// è sostituito dai checkpoint (§7.3).
    /// </summary>
    public bool IsValidChild(uint256 expectedPrevHash, ChainProfile profile)
    {
        if (PrevHash != expectedPrevHash)
            return false;
        if (profile.SkipPowValidation)
            return true;
        var target = new Target(Bits).ToUInt256();
        return Hash <= target;
    }

    /// <summary>Confronto con un checkpoint hardcoded (§7.3).</summary>
    public bool MatchesCheckpoint(Checkpoint checkpoint) =>
        Hash == uint256.Parse(checkpoint.BlockHash);
}
