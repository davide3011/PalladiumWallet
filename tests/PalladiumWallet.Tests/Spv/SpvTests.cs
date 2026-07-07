using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Spv;

namespace PalladiumWallet.Tests.Spv;

public class ScripthashTests
{
    // Vectors computed independently with Python (hashlib), not with NBitcoin.
    [Theory]
    [InlineData("76a9140102030405060708090a0b0c0d0e0f101112131488ac",
        "5546fc69d399ef99854c132abb060381cc159dbec67c496a6f0e0dbf12e83ae8")]
    [InlineData("00140102030405060708090a0b0c0d0e0f1011121314",
        "8639a8f75edb01f890138755277a84283c26fcba6f3289725d19cead464aa78a")]
    public void Lo_scripthash_e_sha256_dello_script_con_byte_invertiti(string scriptHex, string expected)
    {
        var script = Script.FromHex(scriptHex);
        Assert.Equal(expected, Scripthash.FromScript(script));
    }

    [Fact]
    public void Script_identici_producono_scripthash_identico()
    {
        var script = Script.FromHex("76a9140102030405060708090a0b0c0d0e0f101112131488ac");
        Assert.Equal(Scripthash.FromScript(script), Scripthash.FromScript(script));
    }

    [Fact]
    public void Script_diversi_producono_scripthash_diversi()
    {
        var s1 = Script.FromHex("76a9140102030405060708090a0b0c0d0e0f101112131488ac");
        var s2 = Script.FromHex("00140102030405060708090a0b0c0d0e0f1011121314");
        Assert.NotEqual(Scripthash.FromScript(s1), Scripthash.FromScript(s2));
    }
}

public class MerkleProofTests
{
    // Bitcoin block 100000: 4 transactions (even), known merkle root.
    private static readonly uint256[] Block100000Txids =
    [
        uint256.Parse("8c14f0db3df150123e6f3dbbf30f8b955a8249b62ac1d1ff16284aefa3d06d87"),
        uint256.Parse("fff2525b8931402dd09222c50775608f75787bd2b87e56995a7bdd30f79702c4"),
        uint256.Parse("6359f0868171b1d194cbee1af2f16ea598ae8fad666d9b012c8ed2b79a236ec4"),
        uint256.Parse("e9a66845e05d5abc0ad04ec80f774a7e585c6e8db975962d069a522137b80c1d"),
    ];

    private static readonly uint256 Block100000Root =
        uint256.Parse("f3e94742aca4b5ef85488dc37c06c3282295ffec960994b2c0d5ac2a25a95766");

    // ---- even number of transactions (4 tx) ----

    [Fact]
    public void La_radice_calcolata_dalle_foglie_coincide_con_quella_del_blocco()
    {
        Assert.Equal(Block100000Root, MerkleProof.ComputeRootFromLeaves(Block100000Txids));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Ogni_prova_di_merkle_del_blocco_verifica_contro_la_radice(int position)
    {
        var branch = BuildBranch(Block100000Txids, position);
        Assert.True(MerkleProof.Verify(Block100000Txids[position], position, branch, Block100000Root));
    }

    // ---- singola transazione ----

    [Fact]
    public void Radice_con_singola_tx_e_la_tx_stessa()
    {
        var txid = uint256.Parse("8c14f0db3df150123e6f3dbbf30f8b955a8249b62ac1d1ff16284aefa3d06d87");
        var root = MerkleProof.ComputeRootFromLeaves([txid]);
        Assert.Equal(txid, root);
    }

    [Fact]
    public void Verify_con_branch_vuoto_e_posizione_0_e_la_tx_stessa()
    {
        var txid = uint256.Parse("8c14f0db3df150123e6f3dbbf30f8b955a8249b62ac1d1ff16284aefa3d06d87");
        var root = MerkleProof.ComputeRootFromLeaves([txid]);
        Assert.True(MerkleProof.Verify(txid, 0, [], root));
    }

    // ---- odd number of transactions (last one duplicated) ----

    [Fact]
    public void Tre_tx_dispari_la_terza_viene_duplicata()
    {
        // With 3 tx: level 1 = [SHA256d(tx0||tx1), SHA256d(tx2||tx2)]
        // Verify the root is deterministic and correct.
        var txids = Block100000Txids.Take(3).ToArray();
        var root = MerkleProof.ComputeRootFromLeaves(txids);
        var branch2 = BuildBranch(txids, 2);
        Assert.True(MerkleProof.Verify(txids[2], 2, branch2, root));
    }

    [Fact]
    public void Cinque_tx_dispari_verifica_tutte_le_posizioni()
    {
        // 5 tx → even level (4) → even level (2) → root
        var txids = new uint256[5];
        for (var i = 0; i < 5; i++)
            txids[i] = new uint256(new byte[32].Select((_, j) => (byte)(i * 17 + j)).ToArray());
        var root = MerkleProof.ComputeRootFromLeaves(txids);

        for (var pos = 0; pos < 5; pos++)
        {
            var branch = BuildBranch(txids, pos);
            Assert.True(MerkleProof.Verify(txids[pos], pos, branch, root),
                $"posizione {pos} non verifica");
        }
    }

    // ---- two transactions (minimum even) ----

    [Fact]
    public void Due_tx_verifica_entrambe_le_posizioni()
    {
        var txids = Block100000Txids.Take(2).ToArray();
        var root = MerkleProof.ComputeRootFromLeaves(txids);
        for (var pos = 0; pos < 2; pos++)
        {
            var branch = BuildBranch(txids, pos);
            Assert.True(MerkleProof.Verify(txids[pos], pos, branch, root));
        }
    }

    // ---- invalid proofs ----

    [Fact]
    public void Una_prova_per_la_posizione_sbagliata_fallisce()
    {
        var branch = BuildBranch(Block100000Txids, 0);
        Assert.False(MerkleProof.Verify(Block100000Txids[0], 1, branch, Block100000Root));
    }

    [Fact]
    public void Un_txid_estraneo_non_verifica()
    {
        var branch = BuildBranch(Block100000Txids, 0);
        Assert.False(MerkleProof.Verify(uint256.One, 0, branch, Block100000Root));
    }

    [Fact]
    public void Branch_alterato_non_verifica()
    {
        var branch = BuildBranch(Block100000Txids, 0);
        branch[0] = uint256.One; // corrupt the first branch element
        Assert.False(MerkleProof.Verify(Block100000Txids[0], 0, branch, Block100000Root));
    }

    [Fact]
    public void Radice_alterata_non_verifica()
    {
        var branch = BuildBranch(Block100000Txids, 0);
        Assert.False(MerkleProof.Verify(Block100000Txids[0], 0, branch, uint256.One));
    }

    // ---- lista vuota lancia eccezione ----

    [Fact]
    public void Lista_vuota_lancia_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => MerkleProof.ComputeRootFromLeaves([]));
    }

    /// <summary>Builds the Merkle branch for a leaf by reconstructing the tree levels.</summary>
    private static List<uint256> BuildBranch(IReadOnlyList<uint256> leaves, int position)
    {
        var branch = new List<uint256>();
        var level = leaves.ToList();
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

public class BlockHeaderInfoTests
{
    private const string GenesisHeaderHex =
        "0100000000000000000000000000000000000000000000000000000000000000000000003ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a29ab5f49ffff001d1dac2b7c";

    [Fact]
    public void L_header_della_genesi_si_parsa_con_hash_e_merkle_root_attesi()
    {
        var header = BlockHeaderInfo.Parse(GenesisHeaderHex);

        Assert.Equal(ChainProfiles.Mainnet.GenesisHash, header.Hash.ToString());
        Assert.Equal(
            "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b",
            header.MerkleRoot.ToString());
        Assert.Equal(uint256.Zero, header.PrevHash);
        Assert.Equal(1, header.Version);
        Assert.Equal(0x1d00ffffu, header.Bits);
    }

    [Fact]
    public void Il_collegamento_prev_hash_viene_verificato()
    {
        var genesis = BlockHeaderInfo.Parse(GenesisHeaderHex);
        Assert.True(genesis.IsValidChild(uint256.Zero, ChainProfiles.Mainnet));
        Assert.False(genesis.IsValidChild(uint256.One, ChainProfiles.Mainnet));
    }

    [Fact]
    public void Con_skip_pow_la_validazione_non_controlla_il_target()
    {
        var header = BlockHeaderInfo.Parse(GenesisHeaderHex);
        Assert.True(ChainProfiles.Mainnet.SkipPowValidation);
        Assert.True(header.IsValidChild(uint256.Zero, ChainProfiles.Mainnet));
    }

    [Fact]
    public void Header_troncato_lancia_eccezione()
    {
        Assert.ThrowsAny<Exception>(() => BlockHeaderInfo.Parse("0100000000"));
    }

    [Fact]
    public void Header_hex_non_valido_lancia_eccezione()
    {
        Assert.ThrowsAny<Exception>(() => BlockHeaderInfo.Parse("ZZZ"));
    }

    [Fact]
    public void Senza_skip_pow_un_hash_sotto_il_target_passa()
    {
        // Bitcoin's genesis satisfies its own 0x1d00ffff target by construction.
        var header = BlockHeaderInfo.Parse(GenesisHeaderHex);
        var powProfile = ChainProfiles.Mainnet with { SkipPowValidation = false };

        Assert.True(header.IsValidChild(uint256.Zero, powProfile));
    }

    [Fact]
    public void Senza_skip_pow_un_hash_sopra_il_target_viene_rifiutato()
    {
        // Rewrite the genesis bits to an almost-impossible target (0x03000001 →
        // tiny) without re-mining: the untouched hash can no longer satisfy it.
        var raw = Convert.FromHexString(GenesisHeaderHex);
        raw[72] = 0x01; raw[73] = 0x00; raw[74] = 0x00; raw[75] = 0x03;
        var header = BlockHeaderInfo.Parse(raw);
        var powProfile = ChainProfiles.Mainnet with { SkipPowValidation = false };

        Assert.False(header.IsValidChild(uint256.Zero, powProfile));
        // Same header with skip enabled: the target is ignored.
        Assert.True(header.IsValidChild(uint256.Zero, ChainProfiles.Mainnet));
    }
}
