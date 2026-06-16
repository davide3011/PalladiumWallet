using System;
using System.Collections.Generic;
using CsCheck;
using NBitcoin;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Tests;

/// <summary>
/// Property-based tests (CsCheck). Each test generates hundreds of random inputs and
/// verifies that invariant properties hold — crashes, unexpected exceptions, or
/// roundtrip violations are failures.
/// </summary>
public class PropertyTests
{
    // ── reusable generators ───────────────────────────────────────────────────

    private static readonly Gen<string> GenUnit = Gen.OneOf(
        Gen.Const("PLM"), Gen.Const("mPLM"), Gen.Const("µPLM"), Gen.Const("sat"));

    // random uint256 built from 4 ulongs
    private static readonly Gen<uint256> GenTxid =
        Gen.Select(Gen.ULong, Gen.ULong, Gen.ULong, Gen.ULong, (a, b, c, d) =>
        {
            var bytes = new byte[32];
            BitConverter.TryWriteBytes(bytes.AsSpan(0,  8), a);
            BitConverter.TryWriteBytes(bytes.AsSpan(8,  8), b);
            BitConverter.TryWriteBytes(bytes.AsSpan(16, 8), c);
            BitConverter.TryWriteBytes(bytes.AsSpan(24, 8), d);
            return new uint256(bytes);
        });

    // ── CoinAmount ───────────────────────────────────────────────────────────

    /// TryParseIn must never throw on arbitrary input with valid units.
    [Fact]
    public void CoinAmount_TryParseIn_non_lancia_mai_su_input_arbitrario()
    {
        Gen.Select(GenUnit, Gen.String).Sample((unit, text) =>
        {
            try
            {
                CoinAmount.TryParseIn(text, unit, out _);
            }
            catch (ArgumentException)
            {
                // unknown unit: impossible here because we only use known units
                throw;
            }
            catch (Exception ex)
            {
                Assert.Fail($"TryParseIn threw {ex.GetType().Name} for unit={unit}");
            }
        });
    }

    /// FormatIn → TryParseIn: any satoshi value in [0, MaxSupply] must roundtrip exactly.
    [Fact]
    public void CoinAmount_roundtrip_FormatIn_TryParseIn_per_ogni_unita()
    {
        const long MaxSupply = 21_000_000L * 100_000_000L;
        Gen.Select(Gen.Long[0, MaxSupply], GenUnit).Sample((sats, unit) =>
        {
            var formatted = CoinAmount.FormatIn(sats, unit, withLabel: false);
            Assert.True(
                CoinAmount.TryParseIn(formatted, unit, out var parsed),
                $"FormatIn={formatted} unit={unit} failed to re-parse");
            Assert.Equal(sats, parsed);
        });
    }

    /// TryParseCoins must never throw on arbitrary input.
    [Fact]
    public void CoinAmount_TryParseCoins_non_lancia_mai_su_input_arbitrario()
    {
        Gen.String.Sample(text =>
        {
            try
            {
                CoinAmount.TryParseCoins(text, out _);
            }
            catch (Exception ex)
            {
                Assert.Fail($"TryParseCoins threw {ex.GetType().Name}");
            }
        });
    }

    /// Any value accepted by TryParseCoins must be ≥ 0.
    [Fact]
    public void CoinAmount_TryParseCoins_accetta_solo_valori_non_negativi()
    {
        Gen.String.Sample(text =>
        {
            if (CoinAmount.TryParseCoins(text, out var sats))
                Assert.True(sats >= 0, $"TryParseCoins returned {sats} for '{text}'");
        });
    }

    // ── EncryptedFile ─────────────────────────────────────────────────────────

    /// Encrypt → Decrypt with the same password must return the original plaintext.
    [Fact]
    public void EncryptedFile_roundtrip_su_contenuto_e_password_arbitrari()
    {
        Gen.Select(Gen.String, Gen.String[1, 64]).Sample((plaintext, password) =>
        {
            var cipher    = EncryptedFile.Encrypt(plaintext, password);
            var recovered = EncryptedFile.Decrypt(cipher, password);
            Assert.Equal(plaintext, recovered);
        });
    }

    /// Decrypt with the wrong password must throw WrongPasswordException, never anything else.
    [Fact]
    public void EncryptedFile_password_sbagliata_lancia_solo_WrongPasswordException()
    {
        Gen.Select(Gen.String, Gen.String[1, 32], Gen.String[1, 32]).Sample((plaintext, pwd1, pwd2) =>
        {
            if (pwd1 == pwd2) return; // same password: valid roundtrip, skip

            var cipher = EncryptedFile.Encrypt(plaintext, pwd1);
            try
            {
                EncryptedFile.Decrypt(cipher, pwd2);
                Assert.Fail("Decrypt with wrong password did not throw");
            }
            catch (WrongPasswordException) { /* expected */ }
            catch (Exception ex)
            {
                Assert.Fail($"Decrypt threw {ex.GetType().Name} instead of WrongPasswordException");
            }
        });
    }

    /// IsEncrypted must never throw on arbitrary input.
    [Fact]
    public void EncryptedFile_IsEncrypted_non_lancia_mai()
    {
        Gen.String.Sample(s =>
        {
            try { EncryptedFile.IsEncrypted(s); }
            catch (Exception ex)
            {
                Assert.Fail($"IsEncrypted threw {ex.GetType().Name}");
            }
        });
    }

    // ── MerkleProof ───────────────────────────────────────────────────────────

    /// Every leaf of a randomly generated Merkle tree must verify against the root.
    [Fact]
    public void MerkleProof_ogni_foglia_verifica_contro_la_sua_radice()
    {
        GenTxid.Array[1, 16].Sample(txids =>
        {
            var root = MerkleProof.ComputeRootFromLeaves(txids);
            for (var pos = 0; pos < txids.Length; pos++)
            {
                var branch = BuildBranch(txids, pos);
                Assert.True(
                    MerkleProof.Verify(txids[pos], pos, branch, root),
                    $"Verify failed for pos={pos} over {txids.Length} leaves");
            }
        });
    }

    /// A txid not present in the leaves must not verify (and must not crash).
    [Fact]
    public void MerkleProof_txid_estraneo_non_verifica_e_non_crasha()
    {
        Gen.Select(GenTxid.Array[2, 8], GenTxid).Sample((txids, extra) =>
        {
            if (txids.Contains(extra)) return; // random collision: skip

            var root   = MerkleProof.ComputeRootFromLeaves(txids);
            var branch = BuildBranch(txids, 0);
            Assert.False(MerkleProof.Verify(extra, 0, branch, root));
        });
    }

    // helper: builds the branch for the given position
    private static List<uint256> BuildBranch(IReadOnlyList<uint256> leaves, int position)
    {
        var branch = new List<uint256>();
        var level  = leaves.ToList();
        while (level.Count > 1)
        {
            var sibling = (position ^ 1) < level.Count ? level[position ^ 1] : level[position];
            branch.Add(sibling);
            var next = new List<uint256>();
            for (var i = 0; i < level.Count; i += 2)
            {
                var pair = new[] { level[i], i + 1 < level.Count ? level[i + 1] : level[i] };
                next.Add(MerkleProof.ComputeRootFromLeaves(pair));
            }
            level = next;
            position >>= 1;
        }
        return branch;
    }
}
