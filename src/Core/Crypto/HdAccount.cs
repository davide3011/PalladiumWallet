using NBitcoin;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Account HD single-sig (blueprint §4.2/§4.4): chiave estesa di account (xprv,
/// oppure solo xpub = watch-only) + tipo di script + profilo di rete. Deriva
/// indirizzi receiving (change=0) e change (change=1) on-demand per indice.
/// Gli indirizzi si derivano sempre dall'xpub di account (sottopath non-hardened),
/// quindi il watch-only funziona per costruzione. Il keystore completo
/// (cifratura, factory dal file wallet, §4.5) arriva con la persistenza (§8).
/// </summary>
public sealed class HdAccount
{
    private readonly ExtKey? _accountXprv;

    public ScriptKind Kind { get; }
    public ChainProfile Profile { get; }

    /// <summary>Path dell'account relativo alla root (es. 84'/746'/0', o personalizzato).</summary>
    public KeyPath AccountPath { get; }

    /// <summary>
    /// Fingerprint della chiave master: con <see cref="AccountPath"/> forma le
    /// origin info richieste dalle PSBT (§6.5). Zero se ignota (xpub importata
    /// senza metadati).
    /// </summary>
    public HDFingerprint MasterFingerprint { get; }

    public ExtPubKey AccountXpub { get; }

    /// <summary>True se l'account conosce solo la xpub: costruisce ma non firma (§4.5).</summary>
    public bool IsWatchOnly => _accountXprv is null;

    private HdAccount(ExtKey? accountXprv, ExtPubKey accountXpub, ScriptKind kind,
        ChainProfile profile, KeyPath accountPath, HDFingerprint masterFingerprint)
    {
        // Valida subito la mappatura (i tipi multisig non sono supportati qui).
        DerivationPaths.ScriptPubKeyTypeFor(kind);
        _accountXprv = accountXprv;
        AccountXpub = accountXpub;
        Kind = kind;
        Profile = profile;
        AccountPath = accountPath;
        MasterFingerprint = masterFingerprint;
    }

    /// <summary>Caso principale: mnemonica BIP39 (+ passphrase opzionale) → account standard.</summary>
    public static HdAccount FromMnemonic(Mnemonic mnemonic, string? passphrase,
        ScriptKind kind, ChainProfile profile, int account = 0) =>
        FromSeed(Bip39.ToSeed(mnemonic, passphrase), kind, profile, account);

    public static HdAccount FromSeed(byte[] seed, ScriptKind kind, ChainProfile profile, int account = 0) =>
        FromSeed(seed, kind, profile, DerivationPaths.AccountPath(kind, profile, account));

    /// <summary>
    /// Import con derivation path personalizzato (§4.2, Sparrow-like):
    /// <paramref name="kind"/> determina solo il tipo di indirizzo generato.
    /// </summary>
    public static HdAccount FromSeed(byte[] seed, ScriptKind kind, ChainProfile profile, KeyPath accountPath)
    {
        var root = ExtKey.CreateFromSeed(seed);
        // L'account si deriva sempre dalla xprv: i livelli hardened del path
        // non sono derivabili da una xpub.
        var accountXprv = root.Derive(accountPath);
        return new HdAccount(accountXprv, accountXprv.Neuter(), kind, profile,
            accountPath, root.Neuter().PubKey.GetHDFingerPrint());
    }

    /// <summary>
    /// Watch-only da xpub di account (§4.4): fingerprint e path sono noti solo
    /// se forniti da chi importa (servono per le origin info PSBT).
    /// </summary>
    public static HdAccount FromAccountXpub(ExtPubKey accountXpub, ScriptKind kind,
        ChainProfile profile, KeyPath? accountPath = null, HDFingerprint? masterFingerprint = null) =>
        new(null, accountXpub, kind, profile,
            accountPath ?? DerivationPaths.AccountPath(kind, profile),
            masterFingerprint ?? default);

    /// <summary>Import spendibile da xprv di account.</summary>
    public static HdAccount FromAccountXprv(ExtKey accountXprv, ScriptKind kind,
        ChainProfile profile, KeyPath? accountPath = null, HDFingerprint? masterFingerprint = null) =>
        new(accountXprv, accountXprv.Neuter(), kind, profile,
            accountPath ?? DerivationPaths.AccountPath(kind, profile),
            masterFingerprint ?? default);

    public BitcoinAddress GetAddress(bool isChange, int index) =>
        GetPublicKey(isChange, index).GetAddress(
            DerivationPaths.ScriptPubKeyTypeFor(Kind),
            PalladiumNetworks.For(Profile.Kind));

    public BitcoinAddress GetReceiveAddress(int index) => GetAddress(isChange: false, index);

    public BitcoinAddress GetChangeAddress(int index) => GetAddress(isChange: true, index);

    public PubKey GetPublicKey(bool isChange, int index) =>
        AccountXpub.Derive(DerivationPaths.AddressSubPath(isChange, index)).PubKey;

    /// <summary>
    /// Chiave privata estesa di un indirizzo. Lancia se watch-only: nessuna
    /// chiave privata è derivabile dalle sole pubbliche (§17).
    /// </summary>
    public ExtKey GetExtPrivateKey(bool isChange, int index) =>
        (_accountXprv ?? throw new InvalidOperationException("Account watch-only: non può firmare."))
            .Derive(DerivationPaths.AddressSubPath(isChange, index));

    /// <summary>Xpub di account in formato SLIP-132 (xpub/ypub/zpub secondo Kind).</summary>
    public string ToSlip132() => Slip132.Encode(AccountXpub, Kind, Profile);

    /// <summary>Xprv di account in formato SLIP-132. Lancia se watch-only.</summary>
    public string ToSlip132Private() =>
        Slip132.Encode(
            _accountXprv ?? throw new InvalidOperationException("Account watch-only: nessuna chiave privata."),
            Kind, Profile);
}
