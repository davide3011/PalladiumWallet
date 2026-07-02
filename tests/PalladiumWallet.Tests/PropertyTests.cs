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

    // ── Scripthash ────────────────────────────────────────────────────────────

    /// The scripthash must equal an independent SHA-256-and-reverse computation
    /// for any script bytes (cross-check, not a re-run of the same code).
    [Fact]
    public void Scripthash_coincide_con_il_calcolo_indipendente_per_ogni_script()
    {
        Gen.Byte.Array[0, 64].Sample(bytes =>
        {
            var expected = System.Security.Cryptography.SHA256.HashData(bytes);
            Array.Reverse(expected);
            Assert.Equal(
                Convert.ToHexString(expected).ToLowerInvariant(),
                Core.Spv.Scripthash.FromScript(Script.FromBytesUnsafe(bytes)));
        });
    }

    // ── Slip132 ───────────────────────────────────────────────────────────────

    private static readonly Gen<Core.Chain.ScriptKind> GenScriptKind = Gen.OneOfConst(
        Core.Chain.ScriptKind.Legacy,
        Core.Chain.ScriptKind.WrappedSegwit,
        Core.Chain.ScriptKind.NativeSegwit,
        Core.Chain.ScriptKind.WrappedSegwitMultisig,
        Core.Chain.ScriptKind.NativeSegwitMultisig);

    private static readonly Gen<Core.Chain.ChainProfile> GenProfile = Gen.OneOfConst(
        Core.Chain.ChainProfiles.Mainnet, Core.Chain.ChainProfiles.Testnet);

    /// Encode → TryDecodePrivate must roundtrip key bytes and header family
    /// for any key, script kind, and network.
    [Fact]
    public void Slip132_roundtrip_chiave_privata_per_ogni_kind_e_rete()
    {
        Gen.Select(Gen.Byte.Array[32, 64], GenScriptKind, GenProfile).Sample((seed, kind, profile) =>
        {
            var key = ExtKey.CreateFromSeed(seed);
            var encoded = Core.Crypto.Slip132.Encode(key, kind, profile);

            Assert.True(Core.Crypto.Slip132.TryDecodePrivate(encoded, profile, out var decoded, out var decodedKind));
            Assert.Equal(key.ToBytes(), decoded!.ToBytes());
            // Legacy and Taproot share the BIP32 header: compare header families, not enum values.
            Assert.Equal(profile.ExtKeyHeaders[kind], profile.ExtKeyHeaders[decodedKind]);
            // A private key must never decode as public.
            Assert.False(Core.Crypto.Slip132.TryDecodePublic(encoded, profile, out _, out _));
        });
    }

    /// Same roundtrip for the public (watch-only import) side.
    [Fact]
    public void Slip132_roundtrip_chiave_pubblica_per_ogni_kind_e_rete()
    {
        Gen.Select(Gen.Byte.Array[32, 64], GenScriptKind, GenProfile).Sample((seed, kind, profile) =>
        {
            var xpub = ExtKey.CreateFromSeed(seed).Neuter();
            var encoded = Core.Crypto.Slip132.Encode(xpub, kind, profile);

            Assert.True(Core.Crypto.Slip132.TryDecodePublic(encoded, profile, out var decoded, out var decodedKind));
            Assert.Equal(xpub.ToBytes(), decoded!.ToBytes());
            Assert.Equal(profile.ExtKeyHeaders[kind], profile.ExtKeyHeaders[decodedKind]);
            Assert.False(Core.Crypto.Slip132.TryDecodePrivate(encoded, profile, out _, out _));
        });
    }

    /// TryDecode must never throw on arbitrary input strings.
    [Fact]
    public void Slip132_TryDecode_non_lancia_mai_su_input_arbitrario()
    {
        Gen.String.Sample(text =>
        {
            try
            {
                Core.Crypto.Slip132.TryDecodePublic(text, Core.Chain.ChainProfiles.Mainnet, out _, out _);
                Core.Crypto.Slip132.TryDecodePrivate(text, Core.Chain.ChainProfiles.Mainnet, out _, out _);
            }
            catch (Exception ex)
            {
                Assert.Fail($"TryDecode threw {ex.GetType().Name} for '{text}'");
            }
        });
    }

    // ── WalletDocument ────────────────────────────────────────────────────────

    /// ToJson → FromJson must preserve labels and contacts for arbitrary strings
    /// (unicode, quotes, control characters…).
    [Fact]
    public void WalletDocument_roundtrip_json_con_etichette_e_contatti_arbitrari()
    {
        Gen.Select(Gen.Select(Gen.String, Gen.String).Array[0, 8], Gen.String).Sample((pairs, contactName) =>
        {
            // Lone UTF-16 surrogates are not representable in JSON (the writer
            // replaces them): an exact roundtrip is impossible by design, skip.
            if (pairs.Any(p => p.Item1.Any(char.IsSurrogate) || p.Item2.Any(char.IsSurrogate))
                || contactName.Any(char.IsSurrogate))
                return;

            var doc = new WalletDocument
            {
                Network = "regtest",
                ScriptKind = "NativeSegwit",
                AccountPath = "84'/1'/0'",
                AccountXpub = "vpub-test",
                Contacts = { new StoredContact { Name = contactName, Address = "rplm1qtest" } },
            };
            foreach (var (k, v) in pairs)
                doc.Labels[k] = v;

            var restored = WalletDocument.FromJson(doc.ToJson());

            Assert.Equal(doc.Labels, restored.Labels);
            Assert.Equal(contactName, Assert.Single(restored.Contacts).Name);
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
