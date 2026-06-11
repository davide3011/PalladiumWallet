using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.App.ViewModels;

/// <summary>Riga dello storico transazioni per la vista.</summary>
public sealed record HistoryRow(string Conferma, string Importo, string Txid, string Verificata);

/// <summary>
/// ViewModel unico dell'applicazione (wizard §15 ridotto + dashboard):
/// pannello di setup (crea/ripristina/apri) e pannello wallet
/// (saldo, ricevi, storico, invia, server).
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private WalletDocument? _doc;
    private HdAccount? _account;
    private string? _walletPath;
    private string? _password;
    private ElectrumClient? _client;
    private IReadOnlyDictionary<string, Transaction>? _lastTransactions;
    private BuiltTransaction? _pendingSend;

    // ---- selezione rete e stato pannelli ----

    public string[] Networks { get; } = ["mainnet", "testnet", "regtest"];

    [ObservableProperty]
    private string selectedNetwork = "mainnet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupVisible))]
    private bool isWalletOpen;

    public bool IsSetupVisible => !IsWalletOpen;

    [ObservableProperty]
    private bool walletFileExists;

    [ObservableProperty]
    private string statusMessage = "";

    // ---- pannello setup ----

    [ObservableProperty]
    private string mnemonicInput = "";

    [ObservableProperty]
    private string passphraseInput = "";

    [ObservableProperty]
    private string passwordInput = "";

    // ---- pannello wallet ----

    [ObservableProperty]
    private string balanceText = "—";

    [ObservableProperty]
    private string unconfirmedText = "";

    [ObservableProperty]
    private string networkInfo = "";

    [ObservableProperty]
    private string receiveAddress = "";

    [ObservableProperty]
    private string serverInput = "";

    [ObservableProperty]
    private bool useSsl;

    [ObservableProperty]
    private string connectionStatus = "non connesso";

    [ObservableProperty]
    private bool isSyncing;

    public ObservableCollection<HistoryRow> History { get; } = [];

    // ---- pannello invia ----

    [ObservableProperty]
    private string sendTo = "";

    [ObservableProperty]
    private string sendAmount = "";

    [ObservableProperty]
    private string sendFeeRate = "1";

    [ObservableProperty]
    private bool sendAll;

    [ObservableProperty]
    private string sendPreview = "";

    [ObservableProperty]
    private bool hasPendingSend;

    public MainWindowViewModel()
    {
        RefreshSetupState();
    }

    private NetKind Net => Enum.Parse<NetKind>(SelectedNetwork, ignoreCase: true);
    private ChainProfile Profile => ChainProfiles.For(Net);

    partial void OnSelectedNetworkChanged(string value) => RefreshSetupState();

    private void RefreshSetupState()
    {
        WalletFileExists = WalletStore.Exists(AppPaths.DefaultWalletPath(Net));
        StatusMessage = WalletFileExists
            ? "Trovato un wallet esistente: inserisci la password (se impostata) e apri."
            : "Nessun wallet su questa rete: creane uno nuovo o ripristina da seed.";
        ServerInput = $"127.0.0.1:{Profile.DefaultTcpPort}";
    }

    // ---------- comandi setup ----------

    [RelayCommand]
    private void GenerateMnemonic()
    {
        MnemonicInput = Bip39.Generate(MnemonicLength.Twelve).ToString();
        StatusMessage = "Nuova mnemonica generata: SCRIVILA SU CARTA prima di continuare.";
    }

    [RelayCommand]
    private void CreateOrRestore()
    {
        try
        {
            var (doc, account) = WalletLoader.NewFromMnemonic(
                MnemonicInput,
                string.IsNullOrEmpty(PassphraseInput) ? null : PassphraseInput,
                ScriptKind.NativeSegwit,
                Profile);
            var path = AppPaths.DefaultWalletPath(Net);
            var password = string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput;
            WalletStore.Save(doc, path, password);
            OpenLoaded(doc, account, path, password);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenExisting()
    {
        try
        {
            var path = AppPaths.DefaultWalletPath(Net);
            var password = string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput;
            var doc = WalletStore.Load(path, password);
            OpenLoaded(doc, WalletLoader.ToAccount(doc), path, password);
        }
        catch (WrongPasswordException)
        {
            StatusMessage = "Password errata.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    private void OpenLoaded(WalletDocument doc, HdAccount account, string path, string? password)
    {
        _doc = doc;
        _account = account;
        _walletPath = path;
        _password = password;
        MnemonicInput = PassphraseInput = PasswordInput = "";

        NetworkInfo = $"{doc.Network} · {doc.ScriptKind} · m/{doc.AccountPath}"
            + (doc.IsWatchOnly ? " · watch-only" : "");
        ApplyCache(doc.Cache);
        IsWalletOpen = true;
        StatusMessage = doc.Cache is null
            ? "Wallet aperto. Connettiti a un server per sincronizzare."
            : "Wallet aperto (dati dell'ultima sincronizzazione).";
    }

    private void ApplyCache(SyncCache? cache)
    {
        if (_account is null)
            return;
        if (cache is null)
        {
            BalanceText = $"0.00000000 {Profile.CoinUnit}";
            UnconfirmedText = "";
            ReceiveAddress = _account.GetReceiveAddress(0).ToString();
            History.Clear();
            return;
        }
        BalanceText = CoinAmount.Format(cache.ConfirmedSats, Profile.CoinUnit);
        UnconfirmedText = cache.UnconfirmedSats != 0
            ? $"+ {CoinAmount.Format(cache.UnconfirmedSats)} non confermato"
            : "";
        ReceiveAddress = _account.GetReceiveAddress(cache.NextReceiveIndex).ToString();
        History.Clear();
        foreach (var tx in cache.History)
            History.Add(new HistoryRow(
                tx.Height > 0 ? tx.Height.ToString() : "mempool",
                (tx.DeltaSats >= 0 ? "+" : "") + CoinAmount.Format(tx.DeltaSats),
                tx.Txid,
                tx.Verified ? "✓ SPV" : "—"));
    }

    // ---------- comandi wallet ----------

    [RelayCommand]
    private async Task ConnectAndSync()
    {
        if (_account is null || _doc is null)
            return;
        IsSyncing = true;
        StatusMessage = "";
        try
        {
            if (_client is null || !_client.IsConnected)
            {
                var (host, port) = ParseServer();
                ConnectionStatus = $"connessione a {host}:{port}…";
                var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(Net));
                _client = await ElectrumClient.ConnectAsync(host, port, UseSsl, pins);
                _client.NotificationReceived += OnServerNotification;
                _client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
                    ConnectionStatus = "disconnesso");
                ConnectionStatus = $"connesso a {host}:{port}{(UseSsl ? " (TLS)" : "")}";
            }

            var sync = new WalletSynchronizer(_account, _client, _doc.GapLimit);
            sync.Progress += msg => Dispatcher.UIThread.Post(() => StatusMessage = msg);
            var result = await sync.SyncOnceAsync();
            _lastTransactions = result.Transactions;

            _doc.Cache = new SyncCache
            {
                TipHeight = result.TipHeight,
                ConfirmedSats = result.ConfirmedSats,
                UnconfirmedSats = result.UnconfirmedSats,
                NextReceiveIndex = result.NextReceiveIndex,
                NextChangeIndex = result.NextChangeIndex,
                History = [.. result.History],
                Utxos = [.. result.Utxos],
            };
            WalletStore.Save(_doc, _walletPath!, _password);
            ApplyCache(_doc.Cache);
            StatusMessage = $"Sincronizzato: altezza {result.TipHeight}, " +
                $"{result.History.Count} transazioni verificate SPV.";
        }
        catch (CertificatePinMismatchException ex)
        {
            ConnectionStatus = "certificato cambiato";
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ConnectionStatus = "errore";
            StatusMessage = $"Errore: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void OnServerNotification(string method, System.Text.Json.JsonElement payload)
    {
        // Cambiamento su un nostro indirizzo o nuovo blocco: risincronizza.
        if (method is "blockchain.scripthash.subscribe" or "blockchain.headers.subscribe")
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsSyncing)
                    _ = ConnectAndSync();
            });
    }

    [RelayCommand]
    private void ResetCertificates()
    {
        new CertificatePinStore(AppPaths.CertificatePinsPath(Net)).ResetAll();
        StatusMessage = "Certificati SSL azzerati: riprova la connessione.";
    }

    [RelayCommand]
    private async Task PrepareSend()
    {
        if (_account is null || _doc?.Cache is null)
        {
            SendPreview = "Sincronizza prima di inviare.";
            return;
        }
        try
        {
            if (_lastTransactions is null)
            {
                SendPreview = "Connettiti al server e sincronizza prima di inviare.";
                return;
            }

            var destination = BitcoinAddress.Create(SendTo.Trim(), PalladiumNetworks.For(Net));
            long amount = 0;
            if (!SendAll && !CoinAmount.TryParseCoins(SendAmount, out amount))
            {
                SendPreview = "Importo non valido.";
                return;
            }
            if (!decimal.TryParse(SendFeeRate, out var feeRate) || feeRate <= 0)
            {
                SendPreview = "Fee rate non valido.";
                return;
            }

            _pendingSend = new TransactionFactory(_account).Build(
                _doc.Cache.Utxos, _lastTransactions, destination, amount, feeRate,
                _doc.Cache.NextChangeIndex, SendAll);

            SendPreview = $"txid {_pendingSend.Txid[..16]}… · " +
                $"fee {CoinAmount.Format(_pendingSend.Fee.Satoshi, Profile.CoinUnit)} " +
                $"({_pendingSend.Transaction.GetVirtualSize()} vB)" +
                (_pendingSend.Signed ? "" : " · NON firmata (watch-only)");
            HasPendingSend = _pendingSend.Signed;
        }
        catch (Exception ex)
        {
            _pendingSend = null;
            HasPendingSend = false;
            SendPreview = $"Errore: {ex.Message}";
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ConfirmSend()
    {
        if (_pendingSend is null || _client is null)
            return;
        try
        {
            var txid = await _client.BroadcastAsync(_pendingSend.ToHex());
            SendPreview = $"Trasmessa: {txid}";
            SendTo = SendAmount = "";
            _pendingSend = null;
            HasPendingSend = false;
            await ConnectAndSync();
        }
        catch (Exception ex)
        {
            SendPreview = $"Errore broadcast: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseWallet()
    {
        _ = _client?.DisposeAsync().AsTask();
        _client = null;
        _doc = null;
        _account = null;
        _lastTransactions = null;
        _pendingSend = null;
        HasPendingSend = false;
        History.Clear();
        IsWalletOpen = false;
        ConnectionStatus = "non connesso";
        RefreshSetupState();
    }

    private (string Host, int Port) ParseServer()
    {
        var parts = ServerInput.Trim().Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p)
            ? p
            : UseSsl ? Profile.DefaultSslPort : Profile.DefaultTcpPort;
        return (host, port);
    }
}
