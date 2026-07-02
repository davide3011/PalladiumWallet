namespace PalladiumWallet.Core.Chain;

/// <summary>
/// The wallet's three network profiles (blueprint §3). Mainnet values verified
/// against the node source (chainparams.cpp / pow.cpp) per the blueprint.
/// </summary>
public static class ChainProfiles
{
    public static ChainProfile Mainnet { get; } = new()
    {
        Kind = NetKind.Mainnet,
        NetName = "mainnet",
        CoinUnit = "PLM",
        WifPrefix = 0x80,
        AddrP2pkh = 55,   // addresses starting with 'P'
        AddrP2sh = 5,     // addresses starting with '3'
        SegwitHrp = "plm",
        Bolt11Hrp = "plm",
        // Mainnet reuses Bitcoin's genesis (blueprint §3).
        GenesisHash = "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f",
        DefaultTcpPort = 50001,
        DefaultSslPort = 50002,
        NodeP2pPort = 2333,
        Bip44CoinType = 746,
        UriScheme = "palladium",
        ExplorerUrl = "https://explorer.palladium-coin.com/",
        SkipPowValidation = true,
        BlockTimeSeconds = 120,
        CoinbaseMaturity = 120,
        MinConfirmations = 6,
        ExtKeyHeaders = new Dictionary<ScriptKind, ExtKeyHeaders>
        {
            [ScriptKind.Legacy] = new(0x0488ade4, 0x0488b21e),                // xprv / xpub
            [ScriptKind.WrappedSegwit] = new(0x049d7878, 0x049d7cb2),         // yprv / ypub
            [ScriptKind.WrappedSegwitMultisig] = new(0x0295b005, 0x0295b43f), // Yprv / Ypub
            [ScriptKind.NativeSegwit] = new(0x04b2430c, 0x04b24746),          // zprv / zpub
            [ScriptKind.NativeSegwitMultisig] = new(0x02aa7a99, 0x02aa7ed3),  // Zprv / Zpub
            // No SLIP-132 for P2TR: the context is given by the m/86'/… path
            [ScriptKind.Taproot] = new(0x0488ade4, 0x0488b21e),               // xprv / xpub (BIP32 standard)
        },
        // Known indexing servers for first contact (§3/§9); other
        // peers are discovered via server.peers.subscribe.
        BootstrapServers =
        [
            new ServerEndpoint("173.212.224.67", 50001, 50002),
            new ServerEndpoint("144.91.120.225", 50001, 50002),
            new ServerEndpoint("66.94.115.80", 50001, 50002),
            new ServerEndpoint("89.117.149.130", 50001, 50002),
        ],
        // TODO: populate with the chain's real [hash, bits] (§7.3) before release.
        Checkpoints = [],
    };

    public static ChainProfile Testnet { get; } = Mainnet with
    {
        Kind = NetKind.Testnet,
        NetName = "testnet",
        MinConfirmations = 1,
        WifPrefix = 0xff,
        AddrP2pkh = 127,
        AddrP2sh = 115,
        SegwitHrp = "tplm",
        Bolt11Hrp = "tplm",
        // TODO: verify against the node's chainparams.cpp (the blueprint does not report it;
        // here Bitcoin testnet3 genesis is assumed, to be confirmed).
        GenesisHash = "000000000933ea01ad0ee984209779baaec3ced90fa3f408719526f8d77f4943",
        NodeP2pPort = 12333,
        Bip44CoinType = 1,
        ExtKeyHeaders = new Dictionary<ScriptKind, ExtKeyHeaders>
        {
            // tprv/tpub from the blueprint; the testnet SLIP-132 variants (u/v/U/V) are
            // the standard Electrum values.
            [ScriptKind.Legacy] = new(0x04358394, 0x043587cf),                // tprv / tpub
            [ScriptKind.WrappedSegwit] = new(0x044a4e28, 0x044a5262),         // uprv / upub
            [ScriptKind.WrappedSegwitMultisig] = new(0x024285b5, 0x024289ef), // Uprv / Upub
            [ScriptKind.NativeSegwit] = new(0x045f18bc, 0x045f1cf6),          // vprv / vpub
            [ScriptKind.NativeSegwitMultisig] = new(0x02575048, 0x02575483),  // Vprv / Vpub
            [ScriptKind.Taproot] = new(0x04358394, 0x043587cf),               // tprv / tpub (BIP32 standard)
        },
        BootstrapServers = [],
        Checkpoints = [],
    };

    public static ChainProfile Regtest { get; } = Testnet with
    {
        Kind = NetKind.Regtest,
        NetName = "regtest",
        SegwitHrp = "rplm",
        Bolt11Hrp = "rplm",
        // TODO: verify against the node's chainparams.cpp (Bitcoin regtest genesis assumed).
        GenesisHash = "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206",
        NodeP2pPort = 28444,
        Checkpoints = [],
    };

    public static ChainProfile For(NetKind kind) => kind switch
    {
        NetKind.Mainnet => Mainnet,
        NetKind.Testnet => Testnet,
        NetKind.Regtest => Regtest,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
