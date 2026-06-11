using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Spv;

namespace PalladiumWallet.Tests.Spv;

public class ScripthashTests
{
    // Vettori calcolati indipendentemente con Python (hashlib), non con NBitcoin.
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
}

public class MerkleProofTests
{
    // Blocco Bitcoin 100000: 4 transazioni, merkle root nota — àncora esterna
    // per la convenzione di hashing/ordinamento.
    private static readonly uint256[] Block100000Txids =
    [
        uint256.Parse("8c14f0db3df150123e6f3dbbf30f8b955a8249b62ac1d1ff16284aefa3d06d87"),
        uint256.Parse("fff2525b8931402dd09222c50775608f75787bd2b87e56995a7bdd30f79702c4"),
        uint256.Parse("6359f0868171b1d194cbee1af2f16ea598ae8fad666d9b012c8ed2b79a236ec4"),
        uint256.Parse("e9a66845e05d5abc0ad04ec80f774a7e585c6e8db975962d069a522137b80c1d"),
    ];

    private static readonly uint256 Block100000Root =
        uint256.Parse("f3e94742aca4b5ef85488dc37c06c3282295ffec960994b2c0d5ac2a25a95766");

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

    /// <summary>Costruisce il branch per una foglia ricostruendo i livelli dell'albero.</summary>
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
    // Header del blocco genesi di Bitcoin (riusato dalla mainnet PLM, §3).
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
        // La genesi ha PoW valido, ma il punto è che con SkipPowValidation=true
        // (LWMA, §3) il check si limita al collegamento.
        var header = BlockHeaderInfo.Parse(GenesisHeaderHex);
        Assert.True(ChainProfiles.Mainnet.SkipPowValidation);
        Assert.True(header.IsValidChild(uint256.Zero, ChainProfiles.Mainnet));
    }
}
