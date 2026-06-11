using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Schema del file wallet (blueprint §8), versionato per consentire migrazioni
/// automatiche all'apertura. La cifratura (quando c'è una password) avvolge
/// l'intero documento via <see cref="EncryptedFile"/>.
/// </summary>
public sealed class WalletDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Nome rete (mainnet/testnet/regtest), come ChainProfile.NetName.</summary>
    public required string Network { get; set; }

    /// <summary>Tipo di script (nome dell'enum ScriptKind).</summary>
    public required string ScriptKind { get; set; }

    /// <summary>Mnemonica BIP39 in chiaro nel documento (il documento va cifrato!); null se watch-only.</summary>
    public string? Mnemonic { get; set; }

    /// <summary>Extension word BIP39 (§4.1); null se assente.</summary>
    public string? Passphrase { get; set; }

    /// <summary>Path di account (es. "84'/746'/0'").</summary>
    public required string AccountPath { get; set; }

    /// <summary>Xpub di account in SLIP-132: basta da sola per il watch-only.</summary>
    public required string AccountXpub { get; set; }

    public string? MasterFingerprint { get; set; }

    /// <summary>Gap limit per la scansione indirizzi (§5), configurabile.</summary>
    public int GapLimit { get; set; } = 20;

    /// <summary>Etichette per indirizzo/txid (§12).</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Rubrica contatti (nome + indirizzo blockchain).</summary>
    public List<StoredContact> Contacts { get; set; } = [];

    /// <summary>Cache dell'ultimo stato sincronizzato (saldo/storico mostrabili offline).</summary>
    public SyncCache? Cache { get; set; }

    public bool IsWatchOnly => Mnemonic is null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static WalletDocument FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<WalletDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("File wallet non valido.");
        return Migrate(doc);
    }

    /// <summary>Migrazioni di schema all'apertura (§8). Per ora esiste solo la v1.</summary>
    private static WalletDocument Migrate(WalletDocument doc) => doc.Version switch
    {
        CurrentVersion => doc,
        _ => throw new InvalidDataException(
            $"Versione file wallet {doc.Version} non supportata (max {CurrentVersion})."),
    };
}

/// <summary>Contatto in rubrica: nome leggibile + indirizzo blockchain.</summary>
public sealed class StoredContact
{
    public required string Name { get; set; }
    public required string Address { get; set; }
}

/// <summary>Stato sincronizzato persistito: permette di mostrare saldo/storico offline.</summary>
public sealed class SyncCache
{
    public int TipHeight { get; set; }
    public long ConfirmedSats { get; set; }
    public long UnconfirmedSats { get; set; }
    public int NextReceiveIndex { get; set; }
    public int NextChangeIndex { get; set; }
    public List<CachedTx> History { get; set; } = [];
    public List<CachedUtxo> Utxos { get; set; } = [];
    public List<CachedAddress> Addresses { get; set; } = [];
}

/// <summary>Indirizzo scansionato con saldo proprio e numero di transazioni (vista indirizzi).</summary>
public sealed class CachedAddress
{
    public required string Address { get; set; }
    public bool IsChange { get; set; }
    public int Index { get; set; }
    public long BalanceSats { get; set; }
    public int TxCount { get; set; }
}

public sealed class CachedTx
{
    public required string Txid { get; set; }
    public int Height { get; set; }
    public long DeltaSats { get; set; }
    public bool Verified { get; set; }
}

public sealed class CachedUtxo
{
    public required string Txid { get; set; }
    public int Vout { get; set; }
    public long ValueSats { get; set; }
    public required string Address { get; set; }
    public bool IsChange { get; set; }
    public int AddressIndex { get; set; }
    public int Height { get; set; }
    public bool Frozen { get; set; }
}
