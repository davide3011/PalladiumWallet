using NBitcoin;
using NBitcoin.Crypto;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Spv;

/// <summary>
/// Block header parsed from the canonical 80 bytes (blueprint §7.2):
/// version, prev_hash, merkle_root, timestamp, bits, nonce.
/// </summary>
public sealed record BlockHeaderInfo(
    int Version, uint256 PrevHash, uint256 MerkleRoot, uint Timestamp, uint Bits, uint Nonce, uint256 Hash)
{
    public const int Size = 80;

    public static BlockHeaderInfo Parse(string headerHex) => Parse(Convert.FromHexString(headerHex));

    public static BlockHeaderInfo Parse(byte[] raw)
    {
        if (raw.Length != Size)
            throw new ArgumentException($"Header is {raw.Length} bytes (expected {Size}).", nameof(raw));

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
    /// SPV header validation (§7.2): linkage to the previous header and,
    /// if the profile does not mandate skip (LWMA chain), PoW verification
    /// hash &lt;= target from bits. Full bits/retargeting consistency requires
    /// history — for this chain it is replaced by checkpoints (§7.3).
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

    /// <summary>Comparison against a hardcoded checkpoint (§7.3).</summary>
    public bool MatchesCheckpoint(Checkpoint checkpoint) =>
        Hash == uint256.Parse(checkpoint.BlockHash);
}
