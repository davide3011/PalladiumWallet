using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

// CLI del wallet (blueprint §13): i casi d'uso del Core esposti da riga di
// comando, per scripting, test e confronto col wallet di riferimento.

try
{
    return args switch
    {
        ["newseed", .. var rest] => NewSeed(rest),
        ["addresses", var words, .. var rest] => Addresses(words, rest),
        ["create", .. var rest] => Create(rest),
        ["restore", var words, .. var rest] => Restore(words, rest),
        ["restore-xpub", var xpub, .. var rest] => RestoreXpub(xpub, rest),
        ["info", .. var rest] => Info(rest),
        ["sync", .. var rest] => await Sync(rest),
        ["send", .. var rest] => await Send(rest),
        ["reset-certs", .. var rest] => ResetCerts(rest),
        ["servers", .. var rest] => await Servers(rest),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is WalletSpendException or WrongPasswordException
    or CertificatePinMismatchException or ElectrumServerException or SpvVerificationException)
{
    Console.Error.WriteLine($"Errore: {ex.Message}");
    return 1;
}

static int NewSeed(string[] o)
{
    var length = Opt(o, "--words") == "24" ? MnemonicLength.TwentyFour : MnemonicLength.Twelve;
    Console.WriteLine(Bip39.Generate(length));
    return 0;
}

static int Addresses(string words, string[] o)
{
    var account = AccountFromWords(words, o);
    if (account is null)
        return 1;
    var count = int.TryParse(Opt(o, "--count"), out var n) ? n : 5;
    Console.WriteLine($"rete:    {account.Profile.NetName}   tipo: {account.Kind}   path: m/{account.AccountPath}");
    Console.WriteLine($"account: {account.ToSlip132()}");
    for (var i = 0; i < count; i++)
        Console.WriteLine($"  receiving/{i}: {account.GetReceiveAddress(i)}");
    for (var i = 0; i < Math.Min(count, 2); i++)
        Console.WriteLine($"  change/{i}:    {account.GetChangeAddress(i)}");
    return 0;
}

static int Create(string[] o)
{
    var mnemonic = Bip39.Generate(Opt(o, "--words") == "24" ? MnemonicLength.TwentyFour : MnemonicLength.Twelve);
    Console.WriteLine("Nuova mnemonica (scrivila su carta, NON viene rimostrata):");
    Console.WriteLine($"  {mnemonic}");
    return SaveWallet(mnemonic.ToString(), o);
}

static int Restore(string words, string[] o)
{
    if (!Bip39.TryParse(words, out _))
    {
        Console.Error.WriteLine("Mnemonica non valida (parole o checksum errati).");
        return 1;
    }
    return SaveWallet(words.Trim(), o);
}

static int RestoreXpub(string xpubText, string[] o)
{
    var profile = Profile(o);
    if (!Slip132.TryDecodePublic(xpubText, profile, out var xpub, out var kind))
    {
        Console.Error.WriteLine("Chiave estesa non riconosciuta per questa rete.");
        return 1;
    }
    var account = HdAccount.FromAccountXpub(xpub!, kind, profile);
    var doc = new WalletDocument
    {
        Network = profile.NetName,
        ScriptKind = kind.ToString(),
        AccountPath = account.AccountPath.ToString(),
        AccountXpub = account.ToSlip132(),
    };
    var path = WalletPath(o, profile);
    WalletStore.Save(doc, path, Opt(o, "--password"));
    Console.WriteLine($"Wallet watch-only salvato in {path}");
    return 0;
}

static int Info(string[] o)
{
    var (doc, account, path) = OpenWallet(o);
    Console.WriteLine($"file:     {path}");
    Console.WriteLine($"rete:     {doc.Network}   tipo: {doc.ScriptKind}   watch-only: {doc.IsWatchOnly}");
    Console.WriteLine($"path:     m/{doc.AccountPath}");
    Console.WriteLine($"xpub:     {doc.AccountXpub}");
    if (doc.Cache is { } cache)
    {
        Console.WriteLine($"saldo:    {CoinAmount.Format(cache.ConfirmedSats, account.Profile.CoinUnit)} confermato"
            + (cache.UnconfirmedSats != 0 ? $" + {CoinAmount.Format(cache.UnconfirmedSats)} in attesa (non spendibile)." : ""));
        Console.WriteLine($"sync:     altezza {cache.TipHeight}, {cache.History.Count} transazioni");
        Console.WriteLine($"ricezione: {account.GetReceiveAddress(cache.NextReceiveIndex)}");
    }
    else
    {
        Console.WriteLine($"ricezione: {account.GetReceiveAddress(0)}   (mai sincronizzato)");
    }

    if (o.Contains("--addresses") && doc.Cache is { } c)
    {
        Console.WriteLine("indirizzi:");
        foreach (var a in c.Addresses)
            Console.WriteLine($"  {(a.IsChange ? "change " : "ricev. ")}{a.Index,3}  {a.Address}  " +
                $"{CoinAmount.Format(a.BalanceSats),18}  ({a.TxCount} tx)");
    }
    return 0;
}

static async Task<int> Sync(string[] o)
{
    var (doc, account, path) = OpenWallet(o);
    await using var client = await Connect(o, account.Profile);

    var sync = new WalletSynchronizer(account, client, doc.GapLimit);
    sync.Progress += msg => Console.WriteLine($"  {msg}");
    var net = PalladiumNetworks.For(account.Profile.Kind);
    sync.PreloadCaches(
        doc.Cache?.RawTxHex ?? [],
        doc.Cache?.VerifiedAt ?? [],
        doc.Cache?.BlockHeaders,
        doc.Cache?.NextReceiveIndex ?? 0,
        doc.Cache?.NextChangeIndex ?? 0,
        net);
    var result = await sync.SyncOnceAsync();

    var (rawHex, verifiedAt, blockHeaders) = sync.ExportCaches(net);
    doc.Cache = new SyncCache
    {
        TipHeight = result.TipHeight,
        ConfirmedSats = result.ConfirmedSats,
        UnconfirmedSats = result.UnconfirmedSats,
        NextReceiveIndex = result.NextReceiveIndex,
        NextChangeIndex = result.NextChangeIndex,
        History = [.. result.History],
        Utxos = [.. result.Utxos],
        Addresses = [.. result.AddressRows],
        RawTxHex = rawHex,
        VerifiedAt = verifiedAt,
        BlockHeaders = blockHeaders,
    };
    WalletStore.Save(doc, path, Opt(o, "--password"));

    Console.WriteLine($"Saldo: {CoinAmount.Format(result.ConfirmedSats, account.Profile.CoinUnit)} confermato"
        + (result.UnconfirmedSats != 0 ? $" + {CoinAmount.Format(result.UnconfirmedSats)} in attesa di conferma (non spendibile)" : ""));
    Console.WriteLine($"Storico ({result.History.Count}):");
    foreach (var tx in result.History)
        Console.WriteLine($"  {(tx.Height > 0 ? tx.Height.ToString() : "mempool"),7}  " +
            $"{(tx.DeltaSats >= 0 ? "+" : "")}{CoinAmount.Format(tx.DeltaSats)}  {tx.Txid}" +
            (tx.Verified ? "" : "  (non verificata)"));
    return 0;
}

static async Task<int> Send(string[] o)
{
    var (doc, account, path) = OpenWallet(o);
    if (doc.Cache is null)
    {
        Console.Error.WriteLine("Wallet mai sincronizzato: esegui prima 'sync'.");
        return 1;
    }
    var to = Opt(o, "--to") ?? throw new WalletSpendException("--to mancante.");
    var destination = BitcoinAddress.Create(to, PalladiumNetworks.For(account.Profile.Kind));
    var sendAll = o.Contains("--all");
    long amount = 0;
    if (!sendAll && !CoinAmount.TryParseCoins(Opt(o, "--amount") ?? "", out amount))
        throw new WalletSpendException("--amount mancante o non valido (oppure usa --all).");
    var feeRate = decimal.TryParse(Opt(o, "--feerate"), out var fr) ? fr : 1m;

    // Tx di provenienza degli UTXO: servono per importi e firma.
    await using var client = await Connect(o, account.Profile);
    var network = PalladiumNetworks.For(account.Profile.Kind);
    var transactions = new Dictionary<string, Transaction>();
    foreach (var txid in doc.Cache.Utxos.Select(u => u.Txid).Distinct())
        transactions[txid] = Transaction.Parse(await client.GetTransactionAsync(txid), network);

    var built = new TransactionFactory(account).Build(
        doc.Cache.Utxos, transactions, destination, amount, feeRate,
        doc.Cache.NextChangeIndex, sendAll);

    Console.WriteLine($"txid:  {built.Txid}");
    Console.WriteLine($"fee:   {CoinAmount.Format(built.Fee.Satoshi, account.Profile.CoinUnit)} " +
        $"({feeRate} sat/vB, {built.Transaction.GetVirtualSize()} vB)");

    if (!built.Signed)
    {
        Console.WriteLine("Wallet watch-only: PSBT da firmare offline (§6.5):");
        Console.WriteLine(built.Psbt.ToBase64());
        return 0;
    }

    if (!o.Contains("--broadcast"))
    {
        Console.WriteLine("Transazione firmata (NON trasmessa, aggiungi --broadcast):");
        Console.WriteLine(built.ToHex());
        return 0;
    }

    var txid2 = await client.BroadcastAsync(built.ToHex());
    Console.WriteLine($"Trasmessa: {txid2}");
    return 0;
}

static int ResetCerts(string[] o)
{
    var profile = Profile(o);
    new CertificatePinStore(AppPaths.CertificatePinsPath(profile.Kind)).ResetAll();
    Console.WriteLine($"Certificati SSL salvati per {profile.NetName} azzerati.");
    return 0;
}

static async Task<int> Servers(string[] o)
{
    var profile = Profile(o);
    var registry = new ServerRegistry(profile, AppPaths.ServersPath(profile.Kind));

    if (o.Contains("--discover"))
    {
        await using var client = await Connect(o, profile);
        var added = await registry.DiscoverAsync(client);
        Console.WriteLine($"Scoperti {added} nuovi server dai peer.");
    }

    Console.WriteLine($"Server noti ({profile.NetName}):");
    foreach (var server in registry.All)
        Console.WriteLine($"  {server}");
    return 0;
}

// ----- helper comuni -----

static ChainProfile Profile(string[] o) => Opt(o, "--net") switch
{
    "testnet" => ChainProfiles.Testnet,
    "regtest" => ChainProfiles.Regtest,
    _ => ChainProfiles.Mainnet,
};

static ScriptKind Kind(string[] o) => Opt(o, "--kind") switch
{
    "legacy" => ScriptKind.Legacy,
    "wrapped" => ScriptKind.WrappedSegwit,
    _ => ScriptKind.NativeSegwit,
};

static string WalletPath(string[] o, ChainProfile profile) =>
    Opt(o, "--file") ?? AppPaths.DefaultWalletPath(profile.Kind);

static HdAccount? AccountFromWords(string words, string[] o)
{
    if (!Bip39.TryParse(words, out var mnemonic))
    {
        Console.Error.WriteLine("Mnemonica non valida (parole o checksum errati).");
        return null;
    }
    var profile = Profile(o);
    var kind = Kind(o);
    var passphrase = Opt(o, "--passphrase");
    return Opt(o, "--path") is { } path
        ? HdAccount.FromSeed(Bip39.ToSeed(mnemonic!, passphrase), kind, profile, KeyPath.Parse(path))
        : HdAccount.FromMnemonic(mnemonic!, passphrase, kind, profile);
}

static int SaveWallet(string words, string[] o)
{
    var profile = Profile(o);
    var (doc, account) = WalletLoader.NewFromMnemonic(
        words, Opt(o, "--passphrase"), Kind(o), profile,
        Opt(o, "--path") is { } p ? KeyPath.Parse(p) : null);
    var password = Opt(o, "--password");
    if (string.IsNullOrEmpty(password))
        Console.WriteLine("ATTENZIONE: nessuna --password, il seed sarà in chiaro su disco (§17).");
    var path = WalletPath(o, profile);
    WalletStore.Save(doc, path, password);
    Console.WriteLine($"Wallet salvato in {path}");
    Console.WriteLine($"Primo indirizzo: {account.GetReceiveAddress(0)}");
    return 0;
}

static (WalletDocument, IWalletAccount, string) OpenWallet(string[] o)
{
    var path = WalletPath(o, Profile(o));
    if (!WalletStore.Exists(path))
        throw new WalletSpendException($"Nessun wallet in {path}: usa 'create' o 'restore'.");
    var doc = WalletStore.Load(path, Opt(o, "--password"));
    return (doc, WalletLoader.ToAccount(doc), path);
}

static async Task<ElectrumClient> Connect(string[] o, ChainProfile profile)
{
    var useSsl = o.Contains("--ssl");
    string host;
    int port;
    if (Opt(o, "--server") is { } server)
    {
        var parts = server.Split(':');
        host = parts[0];
        port = parts.Length > 1 ? int.Parse(parts[1])
            : useSsl ? profile.DefaultSslPort : profile.DefaultTcpPort;
    }
    else
    {
        // Senza --server si usa il primo server noto (bootstrap §3 o scoperto §9).
        var known = new ServerRegistry(profile, AppPaths.ServersPath(profile.Kind)).Default
            ?? throw new WalletSpendException("Nessun server noto per questa rete: usa --server host:porta.");
        host = known.Host;
        port = known.PortFor(useSsl);
    }

    var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(profile.Kind));
    Console.WriteLine($"Connessione a {host}:{port}{(useSsl ? " (TLS)" : "")}…");
    return await ElectrumClient.ConnectAsync(host, port, useSsl, pins);
}

static string? Opt(string[] options, string name)
{
    var i = Array.IndexOf(options, name);
    return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
}

static int Usage()
{
    Console.WriteLine("""
        PalladiumWallet CLI

        Wallet:
          create        [--words 12|24] [--kind segwit|wrapped|legacy] [--net mainnet|testnet|regtest]
                        [--passphrase W] [--password P] [--file PATH]
          restore       "<mnemonica>" [stesse opzioni di create] [--path m/...]
          restore-xpub  <xpub slip132> [--net ...] [--password P] [--file PATH]   (watch-only)
          info          [--net ...] [--password P] [--file PATH]

        Rete (server di indicizzazione; senza --server usa il primo server noto):
          sync          [--server host[:porta]] [--ssl] [--net ...] [--password P] [--file PATH]
          send          --to INDIRIZZO (--amount X | --all) [--feerate sat/vB]
                        [--server host[:porta]] [--ssl] [--broadcast] [...]
          servers       [--discover] [--server host[:porta]] [--ssl] [--net ...]
          reset-certs   [--net ...]

        Strumenti:
          newseed       [--words 12|24]
          addresses     "<mnemonica>" [--kind ...] [--net ...] [--count N] [--passphrase W] [--path m/...]
        """);
    return 1;
}
