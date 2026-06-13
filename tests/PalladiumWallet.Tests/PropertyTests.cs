using System;
using System.Collections.Generic;
using CsCheck;
using NBitcoin;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Tests;

/// <summary>
/// Property-based tests (CsCheck). Ogni test genera centinaia di input casuali e
/// verifica che le proprietà invarianti reggano — crash, eccezioni non attese, o
/// violazioni di roundtrip sono failures.
/// </summary>
public class PropertyTests
{
    // ── generatori riutilizzabili ─────────────────────────────────────────────

    private static readonly Gen<string> GenUnit = Gen.OneOf(
        Gen.Const("PLM"), Gen.Const("mPLM"), Gen.Const("µPLM"), Gen.Const("sat"));

    // uint256 casuale costruito da 4 ulong
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

    // ── CoinAmount ──────────────────────────────────────────────────────────

    /// TryParseIn non deve mai lanciare eccezioni su input arbitrario con unità valide.
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
                // unità sconosciuta: impossibile qui perché usiamo solo unità note
                throw;
            }
            catch (Exception ex)
            {
                Assert.Fail($"TryParseIn ha lanciato {ex.GetType().Name} per unit={unit}");
            }
        });
    }

    /// FormatIn → TryParseIn: qualsiasi satoshi [0, MaxSupply] deve fare roundtrip esatto.
    [Fact]
    public void CoinAmount_roundtrip_FormatIn_TryParseIn_per_ogni_unita()
    {
        const long MaxSupply = 21_000_000L * 100_000_000L;
        Gen.Select(Gen.Long[0, MaxSupply], GenUnit).Sample((sats, unit) =>
        {
            var formatted = CoinAmount.FormatIn(sats, unit, withLabel: false);
            Assert.True(
                CoinAmount.TryParseIn(formatted, unit, out var parsed),
                $"FormatIn={formatted} unit={unit} non si riparsa");
            Assert.Equal(sats, parsed);
        });
    }

    /// TryParseCoins non deve mai lanciare su input arbitrario.
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
                Assert.Fail($"TryParseCoins ha lanciato {ex.GetType().Name}");
            }
        });
    }

    /// Qualsiasi valore accettato da TryParseCoins deve essere ≥ 0.
    [Fact]
    public void CoinAmount_TryParseCoins_accetta_solo_valori_non_negativi()
    {
        Gen.String.Sample(text =>
        {
            if (CoinAmount.TryParseCoins(text, out var sats))
                Assert.True(sats >= 0, $"TryParseCoins ha restituito {sats} per '{text}'");
        });
    }

    // ── EncryptedFile ────────────────────────────────────────────────────────

    /// Encrypt → Decrypt con la stessa password deve restituire il testo originale.
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

    /// Decrypt con password sbagliata deve lanciare WrongPasswordException, mai altro.
    [Fact]
    public void EncryptedFile_password_sbagliata_lancia_solo_WrongPasswordException()
    {
        Gen.Select(Gen.String, Gen.String[1, 32], Gen.String[1, 32]).Sample((plaintext, pwd1, pwd2) =>
        {
            if (pwd1 == pwd2) return; // stessa password: roundtrip valido, salta

            var cipher = EncryptedFile.Encrypt(plaintext, pwd1);
            try
            {
                EncryptedFile.Decrypt(cipher, pwd2);
                Assert.Fail("Decrypt con password sbagliata non ha lanciato");
            }
            catch (WrongPasswordException) { /* atteso */ }
            catch (Exception ex)
            {
                Assert.Fail($"Decrypt ha lanciato {ex.GetType().Name} invece di WrongPasswordException");
            }
        });
    }

    /// IsEncrypted non deve mai lanciare su input arbitrario.
    [Fact]
    public void EncryptedFile_IsEncrypted_non_lancia_mai()
    {
        Gen.String.Sample(s =>
        {
            try { EncryptedFile.IsEncrypted(s); }
            catch (Exception ex)
            {
                Assert.Fail($"IsEncrypted ha lanciato {ex.GetType().Name}");
            }
        });
    }

    // ── MerkleProof ──────────────────────────────────────────────────────────

    /// Ogni foglia di un albero Merkle generato casualmente deve verificare contro la radice.
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
                    $"Verify fallita per pos={pos} su {txids.Length} foglie");
            }
        });
    }

    /// Un txid non presente nelle foglie non deve verificare (e non deve crashare).
    [Fact]
    public void MerkleProof_txid_estraneo_non_verifica_e_non_crasha()
    {
        Gen.Select(GenTxid.Array[2, 8], GenTxid).Sample((txids, extra) =>
        {
            if (txids.Contains(extra)) return; // collisione casuale: salta

            var root   = MerkleProof.ComputeRootFromLeaves(txids);
            var branch = BuildBranch(txids, 0);
            Assert.False(MerkleProof.Verify(extra, 0, branch, root));
        });
    }

    // helper: costruisce il branch per la posizione data
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
