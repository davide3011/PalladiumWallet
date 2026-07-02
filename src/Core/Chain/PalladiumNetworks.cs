using NBitcoin;
using NBitcoin.DataEncoders;

namespace PalladiumWallet.Core.Chain;

/// <summary>
/// Builds and registers the Palladium NBitcoin <see cref="Network"/> instances from the
/// <see cref="ChainProfiles"/> (blueprint §19.3). All other code obtains the network
/// from here — never from scattered constants.
/// </summary>
/// <summary>
/// Coin identity for NBitcoin: groups the three networks under the PLM code.
/// Getters are lazy, so no recursion occurs during construction.
/// </summary>
public sealed class PalladiumNetworkSet : INetworkSet
{
    public static readonly PalladiumNetworkSet Instance = new();

    private PalladiumNetworkSet() { }

    public string CryptoCode => "PLM";
    public Network Mainnet => PalladiumNetworks.Mainnet;
    public Network Testnet => PalladiumNetworks.Testnet;
    public Network Regtest => PalladiumNetworks.Regtest;

    public Network GetNetwork(ChainName chainName)
    {
        if (chainName == ChainName.Mainnet) return Mainnet;
        if (chainName == ChainName.Testnet) return Testnet;
        if (chainName == ChainName.Regtest) return Regtest;
        throw new ArgumentOutOfRangeException(nameof(chainName));
    }
}

public static class PalladiumNetworks
{
    // Bitcoin genesis block (Palladium mainnet reuses it, blueprint §3).
    private const string MainGenesisHex =
        "0100000000000000000000000000000000000000000000000000000000000000000000003ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a29ab5f49ffff001d1dac2b7c" +
        Coinbase;

    // Bitcoin testnet3/regtest genesis: same coinbase, different time/bits/nonce.
    // TODO: confirm against the node's chainparams.cpp (see ChainProfiles).
    private const string TestGenesisHex =
        "0100000000000000000000000000000000000000000000000000000000000000000000003ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4adae5494dffff001d1aa4ae18" +
        Coinbase;

    private const string RegtestGenesisHex =
        "0100000000000000000000000000000000000000000000000000000000000000000000003ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4adae5494dffff7f2002000000" +
        Coinbase;

    private const string Coinbase =
        "0101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff4d04ffff001d0104455468652054696d65732030332f4a616e2f32303039204368616e63656c6c6f72206f6e206272696e6b206f66207365636f6e64206261696c6f757420666f722062616e6b73ffffffff0100f2052a01000000434104678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38c4f35504e51ec112de5c384df7ba0b8d578a4c702b6bf11d5fac00000000";

    private static readonly Lazy<Network> _mainnet = new(() => Build(ChainProfiles.Mainnet, MainGenesisHex, 0x504c4d4du));
    private static readonly Lazy<Network> _testnet = new(() => Build(ChainProfiles.Testnet, TestGenesisHex, 0x504c4d54u));
    private static readonly Lazy<Network> _regtest = new(() => Build(ChainProfiles.Regtest, RegtestGenesisHex, 0x504c4d52u));

    public static Network Mainnet => _mainnet.Value;
    public static Network Testnet => _testnet.Value;
    public static Network Regtest => _regtest.Value;

    public static Network For(NetKind kind) => kind switch
    {
        NetKind.Mainnet => Mainnet,
        NetKind.Testnet => Testnet,
        NetKind.Regtest => Regtest,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Network Build(ChainProfile p, string genesisHex, uint magic)
    {
        var legacy = p.ExtKeyHeaders[ScriptKind.Legacy];

        var builder = new NetworkBuilder()
            .SetName($"plm-{p.NetName}")
            .SetNetworkSet(PalladiumNetworkSet.Instance)
            .SetChainName(p.Kind switch
            {
                NetKind.Mainnet => ChainName.Mainnet,
                NetKind.Testnet => ChainName.Testnet,
                _ => ChainName.Regtest,
            })
            // P2P magic is a placeholder only: the SPV wallet never opens P2P connections
            // (it only talks to the indexing server, §3). Required by NBitcoin to
            // distinguish registered networks.
            .SetMagic(magic)
            .SetPort(p.NodeP2pPort)
            .SetRPCPort(p.NodeP2pPort + 1)
            .SetGenesis(genesisHex)
            .SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, [p.AddrP2pkh])
            .SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, [p.AddrP2sh])
            .SetBase58Bytes(Base58Type.SECRET_KEY, [p.WifPrefix])
            // NBitcoin handles only one BIP32 header pair per network: the standard
            // xprv/xpub. SLIP-132 variants (y/z/Y/Z) are serialised manually via
            // ChainProfile.ExtKeyHeaders when needed.
            .SetBase58Bytes(Base58Type.EXT_SECRET_KEY, legacy.PrivateBytes())
            .SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, legacy.PublicBytes())
            .SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, Encoders.Bech32(p.SegwitHrp))
            .SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, Encoders.Bech32(p.SegwitHrp))
            .SetBech32(Bech32Type.TAPROOT_ADDRESS, Encoders.Bech32(p.SegwitHrp))
            .SetUriScheme(p.UriScheme)
            .SetConsensus(new Consensus
            {
                // Values used only by NBitcoin APIs that require them: real header
                // validation is custom (Core/Spv) with PoW skip + checkpoints (§7).
                PowLimit = new Target(new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
                PowTargetSpacing = TimeSpan.FromSeconds(p.BlockTimeSeconds),
                PowTargetTimespan = TimeSpan.FromDays(14),
                PowAllowMinDifficultyBlocks = p.Kind != NetKind.Mainnet,
                PowNoRetargeting = p.Kind == NetKind.Regtest,
                SubsidyHalvingInterval = 210000,
                MajorityEnforceBlockUpgrade = 750,
                MajorityRejectBlockOutdated = 950,
                MajorityWindow = 1000,
                MinimumChainWork = uint256.Zero,
                CoinbaseMaturity = 120,
                SupportSegwit = true,
                SupportTaproot = true,
                ConsensusFactory = new ConsensusFactory(),
            });

        return builder.BuildAndRegister();
    }
}
