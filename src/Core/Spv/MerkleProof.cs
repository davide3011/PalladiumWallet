using NBitcoin;
using NBitcoin.Crypto;

namespace PalladiumWallet.Core.Spv;

/// <summary>
/// Verifica delle prove di Merkle (blueprint §7.1/§7.4): le risposte dei server
/// non sono fidate, ogni transazione confermata va verificata contro il
/// merkle_root dell'header al suo blocco (§17).
/// </summary>
public static class MerkleProof
{
    /// <summary>
    /// Risale la prova dal txid alla radice: a ogni livello il nodo fratello
    /// si concatena a sinistra o destra secondo il bit di posizione, con
    /// doppio SHA-256. I nodi sono nell'ordine restituito dal server
    /// (dal basso verso l'alto), in hex display (byte invertiti).
    /// </summary>
    public static uint256 ComputeRoot(uint256 txid, int position, IEnumerable<uint256> branch)
    {
        var hash = txid;
        Span<byte> data = stackalloc byte[64];
        foreach (var sibling in branch)
        {
            if ((position & 1) == 0)
            {
                hash.ToBytes(data[..32]);
                sibling.ToBytes(data[32..]);
            }
            else
            {
                sibling.ToBytes(data[..32]);
                hash.ToBytes(data[32..]);
            }
            hash = new uint256(Hashes.DoubleSHA256RawBytes(data.ToArray(), 0, 64));
            position >>= 1;
        }
        return hash;
    }

    public static bool Verify(uint256 txid, int position, IEnumerable<uint256> branch, uint256 merkleRoot) =>
        ComputeRoot(txid, position, branch) == merkleRoot;

    /// <summary>Radice di Merkle di una lista completa di txid (per i test e per blocchi piccoli).</summary>
    public static uint256 ComputeRootFromLeaves(IReadOnlyList<uint256> txids)
    {
        if (txids.Count == 0)
            throw new ArgumentException("Serve almeno una foglia.", nameof(txids));

        var level = txids.ToList();
        while (level.Count > 1)
        {
            var next = new List<uint256>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                var left = level[i];
                var right = i + 1 < level.Count ? level[i + 1] : level[i]; // dispari: duplica l'ultimo
                var data = new byte[64];
                left.ToBytes(data.AsSpan(..32));
                right.ToBytes(data.AsSpan(32..));
                next.Add(new uint256(Hashes.DoubleSHA256RawBytes(data, 0, 64)));
            }
            level = next;
        }
        return level[0];
    }
}
