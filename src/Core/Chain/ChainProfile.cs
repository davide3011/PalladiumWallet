namespace PalladiumWallet.Core.Chain;

/// <summary>
/// Tipo di rete supportato (selettore unico per tutto il wallet, blueprint §3).
/// </summary>
public enum NetKind
{
    Mainnet,
    Testnet,
    Regtest,
}

/// <summary>
/// Tipo di script/indirizzo supportato (blueprint §4.3).
/// </summary>
public enum ScriptKind
{
    /// <summary>P2PKH legacy.</summary>
    Legacy,

    /// <summary>P2SH-P2WPKH (segwit wrapped).</summary>
    WrappedSegwit,

    /// <summary>P2WPKH (native segwit) — default consigliato.</summary>
    NativeSegwit,

    /// <summary>P2SH-P2WSH (multisig wrapped).</summary>
    WrappedSegwitMultisig,

    /// <summary>P2WSH (multisig native).</summary>
    NativeSegwitMultisig,
}

/// <summary>
/// Coppia di header di versione BIP32 (4 byte big-endian) per serializzare
/// xprv/xpub e varianti SLIP-132 (y/z/Y/Z) — blueprint §3.
/// </summary>
public readonly record struct ExtKeyHeaders(uint Private, uint Public)
{
    /// <summary>Header privato come 4 byte big-endian (da anteporre al payload BIP32).</summary>
    public byte[] PrivateBytes() => ToBytes(Private);

    /// <summary>Header pubblico come 4 byte big-endian.</summary>
    public byte[] PublicBytes() => ToBytes(Public);

    internal static byte[] ToBytes(uint header) =>
    [
        (byte)(header >> 24),
        (byte)(header >> 16),
        (byte)(header >> 8),
        (byte)header,
    ];
}

/// <summary>
/// Server di indicizzazione di bootstrap: host + porta TCP + porta SSL (blueprint §3/§9).
/// </summary>
public readonly record struct ServerEndpoint(string Host, int TcpPort, int SslPort);

/// <summary>
/// Checkpoint di catena: ancora la fiducia quando la verifica PoW è disattivata
/// (blueprint §7.3). Target serializzato come "bits" compatti.
/// </summary>
public readonly record struct Checkpoint(int Height, string BlockHash, uint Bits);

/// <summary>
/// Profilo di rete/catena (blueprint §3): tutte le costanti specifiche della catena,
/// centralizzate in un solo punto. Nessun magic number altrove nel codice.
/// </summary>
public sealed record ChainProfile
{
    public required NetKind Kind { get; init; }

    /// <summary>Nome rete, usato anche come sottocartella dati.</summary>
    public required string NetName { get; init; }

    /// <summary>Simbolo dell'unità (es. PLM).</summary>
    public required string CoinUnit { get; init; }

    /// <summary>Prefisso WIF delle chiavi private.</summary>
    public required byte WifPrefix { get; init; }

    /// <summary>Byte di versione indirizzi P2PKH.</summary>
    public required byte AddrP2pkh { get; init; }

    /// <summary>Byte di versione indirizzi P2SH.</summary>
    public required byte AddrP2sh { get; init; }

    /// <summary>Human-readable part bech32/bech32m.</summary>
    public required string SegwitHrp { get; init; }

    /// <summary>HRP fatture BOLT11 (Lightning, fase successiva — §11).</summary>
    public required string Bolt11Hrp { get; init; }

    /// <summary>Hash del blocco genesi (hex, byte order da explorer).</summary>
    public required string GenesisHash { get; init; }

    /// <summary>Porta TCP del server di indicizzazione (non è la porta P2P del nodo).</summary>
    public required int DefaultTcpPort { get; init; }

    /// <summary>Porta SSL del server di indicizzazione.</summary>
    public required int DefaultSslPort { get; init; }

    /// <summary>Porta P2P del nodo — solo informativa: il wallet SPV non la usa.</summary>
    public required int NodeP2pPort { get; init; }

    /// <summary>Coin type SLIP-0044 nei derivation path (convenzione wallet).</summary>
    public required int Bip44CoinType { get; init; }

    /// <summary>Schema URI pagamenti BIP21 (senza i due punti).</summary>
    public required string UriScheme { get; init; }

    /// <summary>Block explorer di default.</summary>
    public required string ExplorerUrl { get; init; }

    /// <summary>
    /// La catena usa LWMA (retargeting per-blocco, 2 minuti): un client SPV non può
    /// ricalcolarla, quindi la verifica bits/target va saltata e la fiducia ancorata
    /// ai checkpoint (§3/§7). Per la catena di riferimento è sempre true.
    /// </summary>
    public required bool SkipPowValidation { get; init; }

    /// <summary>Tempo di blocco target in secondi (LWMA v2: 120s).</summary>
    public required int BlockTimeSeconds { get; init; }

    /// <summary>Header BIP32/SLIP-132 per ciascun tipo di script.</summary>
    public required IReadOnlyDictionary<ScriptKind, ExtKeyHeaders> ExtKeyHeaders { get; init; }

    /// <summary>Server di indicizzazione per il primo contatto (bootstrap).</summary>
    public required IReadOnlyList<ServerEndpoint> BootstrapServers { get; init; }

    /// <summary>Checkpoint hardcoded, aggiornati a ogni release (§7.3).</summary>
    public required IReadOnlyList<Checkpoint> Checkpoints { get; init; }
}
