using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBitcoin;
using PalladiumWallet.App.Localization;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.App.ViewModels;

/// <summary>Riga dello storico transazioni per la vista.</summary>
public sealed record HistoryRow(string Conferma, string Importo, string Txid, string Verificata);

/// <summary>Riga della vista indirizzi con chiavi e derivation path pre-calcolati.</summary>
public sealed record AddressRow(
    string Tipo, int Indice, string Indirizzo, string Saldo, string NumTx,
    bool IsChange = false, string PubKey = "", string PrivKey = "", string DerivPath = "")
{
    public bool HasPrivKey => !string.IsNullOrEmpty(PrivKey);
}

/// <summary>Dati completi di un indirizzo passati alla finestra di dettaglio.</summary>
public sealed record AddressInfo(
    Localization.Loc Loc,
    string Address, string DerivPath, string PubKey, string PrivKey)
{
    public bool HasPrivKey => !string.IsNullOrEmpty(PrivKey);
}

/// <summary>Contatto in rubrica: nome + indirizzo blockchain.</summary>
public sealed record ContactEntry(string Name, string Address);

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
    private WalletSynchronizer? _synchronizer;
    private IReadOnlyDictionary<string, Transaction>? _lastTransactions;
    private BuiltTransaction? _pendingSend;

    /// <summary>Notifica arrivata durante una sync: si risincronizza appena finita.</summary>
    private bool _resyncRequested;

    /// <summary>Configurazione globale (§8): lingua e unità.</summary>
    private AppConfig _config = AppConfig.Load();

    /// <summary>Istanza Loc corrente: viene rimpiazzata ad ogni cambio lingua
    /// così Avalonia vede un riferimento diverso e rivaluta le binding {Binding Loc[chiave]}.</summary>
    private Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    /// <summary>Unità corrente per il campo importo del pannello Invia.</summary>
    public string UnitLabel => _config.Unit;

    public AppConfig CurrentConfig => _config;

    // Spunte del menu Impostazioni (ToggleType Radio).
    public bool IsLangIt => _config.Language == "it";
    public bool IsLangEn => _config.Language == "en";
    public bool IsLangEs => _config.Language == "es";
    public bool IsLangFr => _config.Language == "fr";
    public bool IsLangPt => _config.Language == "pt";
    public bool IsLangDe => _config.Language == "de";
    public bool IsUnitPlm => _config.Unit == "PLM";
    public bool IsUnitMilli => _config.Unit == "mPLM";
    public bool IsUnitMicro => _config.Unit == "µPLM";
    public bool IsUnitSat => _config.Unit == "sat";

    [RelayCommand]
    private void SetLanguage(string language)
    {
        _config.Language = language;
        ApplySettings(_config);
    }

    [RelayCommand]
    private void SetUnit(string unit)
    {
        _config.Unit = unit;
        ApplySettings(_config);
    }

    /// <summary>Applica e persiste le impostazioni (§8).</summary>
    public void ApplySettings(AppConfig config)
    {
        _config = config;
        _config.Save();
        _loc = Loc.SwitchTo(config.Language);
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(UnitLabel));
        OnPropertyChanged(nameof(IsLangIt));
        OnPropertyChanged(nameof(IsLangEn));
        OnPropertyChanged(nameof(IsLangEs));
        OnPropertyChanged(nameof(IsLangFr));
        OnPropertyChanged(nameof(IsLangPt));
        OnPropertyChanged(nameof(IsLangDe));
        OnPropertyChanged(nameof(IsUnitPlm));
        OnPropertyChanged(nameof(IsUnitMilli));
        OnPropertyChanged(nameof(IsUnitMicro));
        OnPropertyChanged(nameof(IsUnitSat));
        ApplyCache(_doc?.Cache);
        StatusMessage = Loc.Tr("msg.settings.saved");
    }

    /// <summary>Formatta un importo nell'unità scelta nelle impostazioni.</summary>
    private string Fmt(long sats, bool withLabel = true) =>
        CoinAmount.FormatIn(sats, _config.Unit, withLabel);

    /// <summary>Chiave privata WIF per un indirizzo; stringa vuota se watch-only.</summary>
    private string KeyWif(bool isChange, int index)
    {
        if (_account is null or { IsWatchOnly: true }) return "";
        try
        {
            return _account.GetExtPrivateKey(isChange, index)
                .PrivateKey.GetWif(PalladiumNetworks.For(Net)).ToString();
        }
        catch { return ""; }
    }

    /// <summary>File in attesa di password (apertura da menu File → Apri).</summary>
    private string? _pendingOpenPath;

    /// <summary>Dopo la prima connessione riuscita il timer riconnette da solo.</summary>
    private bool _autoReconnect;

    private readonly DispatcherTimer _keepAliveTimer;

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

    // ---- wizard di setup (§15): un passo alla volta ----

    public const string StepStart = "start";
    public const string StepOpen = "open";
    public const string StepShowSeed = "show-seed";
    public const string StepConfirmSeed = "confirm-seed";
    public const string StepWords = "words";
    public const string StepPassphrase = "passphrase";
    public const string StepPassword = "password";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepStart))]
    [NotifyPropertyChangedFor(nameof(IsStepOpen))]
    [NotifyPropertyChangedFor(nameof(IsStepShowSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepConfirmSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepWords))]
    [NotifyPropertyChangedFor(nameof(IsStepPassphrase))]
    [NotifyPropertyChangedFor(nameof(IsStepPassword))]
    private string setupStep = StepStart;

    public bool IsStepStart => SetupStep == StepStart;
    public bool IsStepOpen => SetupStep == StepOpen;
    public bool IsStepShowSeed => SetupStep == StepShowSeed;
    public bool IsStepConfirmSeed => SetupStep == StepConfirmSeed;
    public bool IsStepWords => SetupStep == StepWords;
    public bool IsStepPassphrase => SetupStep == StepPassphrase;
    public bool IsStepPassword => SetupStep == StepPassword;

    /// <summary>True quando il flusso è "ripristina" (parole inserite dall'utente).</summary>
    private bool _isRestoreFlow;

    [ObservableProperty]
    private string mnemonicInput = "";

    [ObservableProperty]
    private string confirmMnemonicInput = "";

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
    private string serverHost = "";

    [ObservableProperty]
    private string serverPort = "";

    /// <summary>Si predilige TLS: la connessione automatica all'apertura del
    /// wallet usa la porta SSL del server. L'utente può disattivarlo.</summary>
    [ObservableProperty]
    private bool useSsl = true;

    /// <summary>Overlay impostazioni server: aperto da menu Impostazioni → Server.</summary>
    [ObservableProperty]
    private bool isServerSettingsOpen;

    [RelayCommand]
    private void OpenServerSettings() => IsServerSettingsOpen = true;

    [RelayCommand]
    private void CloseServerSettings() => IsServerSettingsOpen = false;

    [ObservableProperty]
    private string connectionStatus = Loc.Tr("conn.none");

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private bool isSyncing;

    public ObservableCollection<KnownServer> KnownServers { get; } = [];

    [ObservableProperty]
    private KnownServer? selectedKnownServer;

    public ObservableCollection<HistoryRow> History { get; } = [];

    public ObservableCollection<AddressRow> Addresses { get; } = [];

    // ---- tab indirizzi ----

    [ObservableProperty]
    private AddressRow? selectedAddressRow;

    /// <summary>Dettaglio indirizzo mostrato nell'overlay in-app; null = nascosto.</summary>
    [ObservableProperty]
    private AddressInfo? addressInfo;

    /// <summary>Apre l'overlay con i dati dell'indirizzo passato.</summary>
    public void ShowAddressInfo(AddressRow row) =>
        AddressInfo = new AddressInfo(Loc, row.Indirizzo, row.DerivPath, row.PubKey, row.PrivKey);

    [RelayCommand]
    private void CloseAddressInfo() => AddressInfo = null;

    // ---- rubrica contatti ----

    public ObservableCollection<ContactEntry> Contacts { get; } = [];

    [ObservableProperty]
    private ContactEntry? selectedContactInList;

    /// <summary>Contatto selezionato nella ComboBox del pannello Invia: riempie SendTo.</summary>
    [ObservableProperty]
    private ContactEntry? sendToContact;

    partial void OnSendToContactChanged(ContactEntry? value)
    {
        if (value is not null)
            SendTo = value.Address;
    }

    [ObservableProperty]
    private string newContactName = "";

    [ObservableProperty]
    private string newContactAddress = "";

    [RelayCommand]
    private void AddContact()
    {
        var name = NewContactName.Trim();
        var addr = NewContactAddress.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(addr)) return;
        Contacts.Add(new ContactEntry(name, addr));
        NewContactName = NewContactAddress = "";
        PersistContacts();
    }

    [RelayCommand]
    private void RemoveSelectedContact()
    {
        if (SelectedContactInList is { } c)
        {
            Contacts.Remove(c);
            SelectedContactInList = null;
            PersistContacts();
        }
    }

    private void PersistContacts()
    {
        if (_doc is null || _walletPath is null) return;
        _doc.Contacts = Contacts
            .Select(c => new PalladiumWallet.Core.Storage.StoredContact { Name = c.Name, Address = c.Address })
            .ToList();
        WalletStore.Save(_doc, _walletPath, _password);
    }

    private void LoadContacts()
    {
        Contacts.Clear();
        if (_doc is null) return;
        foreach (var c in _doc.Contacts)
            Contacts.Add(new ContactEntry(c.Name, c.Address));
    }

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
        _loc = Loc.SwitchTo(_config.Language);
        RefreshSetupState();
        // Aggiornamenti continui (§9): ping periodico per tenere viva la
        // connessione e accorgersi subito della caduta; se cade, riconnette
        // e risincronizza da solo. Le notifiche push restano la via principale.
        _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _keepAliveTimer.Tick += async (_, _) => await KeepAliveTickAsync();
        _keepAliveTimer.Start();
    }

    private async Task KeepAliveTickAsync()
    {
        if (!IsWalletOpen || IsSyncing)
            return;
        if (_client is { IsConnected: true })
        {
            try
            {
                await _client.PingAsync();
            }
            catch
            {
                // La caduta viene gestita dall'evento Disconnected.
            }
        }
        else if (_autoReconnect)
        {
            ConnectionStatus = Loc.Tr("conn.reconnecting");
            await ConnectAndSync();
        }
    }

    private NetKind Net => Enum.Parse<NetKind>(SelectedNetwork, ignoreCase: true);
    private ChainProfile Profile => ChainProfiles.For(Net);

    partial void OnSelectedNetworkChanged(string value) => RefreshSetupState();

    private ServerRegistry Registry => new(Profile, AppPaths.ServersPath(Net));

    private void RefreshSetupState()
    {
        SetupStep = StepStart;
        MnemonicInput = ConfirmMnemonicInput = PassphraseInput = PasswordInput = "";
        WalletFileExists = WalletStore.Exists(AppPaths.DefaultWalletPath(Net));
        StatusMessage = WalletFileExists
            ? Loc.Tr("msg.welcome.existing")
            : Loc.Tr("msg.welcome.new");
        RefreshServers();
    }

    private void RefreshServers()
    {
        KnownServers.Clear();
        foreach (var server in Registry.All)
            KnownServers.Add(server);
        SelectedKnownServer = KnownServers.FirstOrDefault();
        if (SelectedKnownServer is null)
        {
            ServerHost = "127.0.0.1";
            ServerPort = (UseSsl ? Profile.DefaultSslPort : Profile.DefaultTcpPort).ToString();
        }
    }

    /// <summary>Evita la ricorsione tra OnUseSslChanged e OnServerPortChanged
    /// mentre tengono coerenti porta e flag TLS.</summary>
    private bool _syncingServerFields;

    partial void OnSelectedKnownServerChanged(KnownServer? value)
    {
        if (value is null)
            return;
        _syncingServerFields = true;
        ServerHost = value.Host;
        ServerPort = value.PortFor(UseSsl).ToString();
        _syncingServerFields = false;
    }

    partial void OnUseSslChanged(bool value)
    {
        if (_syncingServerFields)
            return;
        // Attivare/disattivare TLS allinea la porta: porta SSL del server
        // selezionato (o default di profilo) quando TLS è on, porta TCP quando off.
        _syncingServerFields = true;
        ServerPort = SelectedKnownServer is { } server
            ? server.PortFor(value).ToString()
            : (value ? Profile.DefaultSslPort : Profile.DefaultTcpPort).ToString();
        _syncingServerFields = false;
    }

    partial void OnServerPortChanged(string value)
    {
        if (_syncingServerFields)
            return;
        if (!int.TryParse(value.Trim(), out var port))
            return;
        // Scegliere una porta nota allinea il flag TLS, così non si tenta mai
        // una connessione in chiaro sulla porta SSL (causa di "connection error").
        bool? wantSsl =
            SelectedKnownServer is { } s && port == s.SslPort ? true :
            SelectedKnownServer is { } t && port == t.TcpPort ? false :
            port == Profile.DefaultSslPort ? true :
            port == Profile.DefaultTcpPort ? false :
            null;
        if (wantSsl is bool b && b != UseSsl)
        {
            _syncingServerFields = true;
            UseSsl = b;
            _syncingServerFields = false;
        }
    }

    // ---------- comandi del wizard (§15): un passo alla volta ----------

    [RelayCommand]
    private void WizardStartOpen()
    {
        PasswordInput = "";
        SetupStep = StepOpen;
        StatusMessage = Loc.Tr("msg.open.password");
    }

    [RelayCommand]
    private void WizardStartNew()
    {
        _isRestoreFlow = false;
        MnemonicInput = Bip39.Generate(MnemonicLength.Twelve).ToString();
        SetupStep = StepShowSeed;
        StatusMessage = Loc.Tr("msg.seed.write");
    }

    [RelayCommand]
    private void WizardStartRestore()
    {
        _isRestoreFlow = true;
        MnemonicInput = "";
        SetupStep = StepWords;
        StatusMessage = Loc.Tr("msg.words.enter");
    }

    [RelayCommand]
    private void WizardNextFromShowSeed()
    {
        ConfirmMnemonicInput = "";
        SetupStep = StepConfirmSeed;
        StatusMessage = Loc.Tr("msg.seed.retype");
    }

    [RelayCommand]
    private void WizardNextFromConfirmSeed()
    {
        var normalized = string.Join(' ',
            ConfirmMnemonicInput.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!string.Equals(normalized, MnemonicInput, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = Loc.Tr("msg.seed.mismatch");
            return;
        }
        GoToPassphraseStep();
    }

    [RelayCommand]
    private void WizardNextFromWords()
    {
        if (!Bip39.TryParse(MnemonicInput, out _))
        {
            StatusMessage = Loc.Tr("msg.words.invalid");
            return;
        }
        GoToPassphraseStep();
    }

    private void GoToPassphraseStep()
    {
        PassphraseInput = "";
        SetupStep = StepPassphrase;
        StatusMessage = Loc.Tr("msg.passphrase.info");
    }

    [RelayCommand]
    private void WizardNextFromPassphrase()
    {
        PasswordInput = "";
        SetupStep = StepPassword;
        StatusMessage = Loc.Tr("msg.password.info");
    }

    [RelayCommand]
    private void WizardBack()
    {
        SetupStep = SetupStep switch
        {
            StepOpen or StepShowSeed or StepWords => StepStart,
            StepConfirmSeed => StepShowSeed,
            StepPassphrase => _isRestoreFlow ? StepWords : StepConfirmSeed,
            StepPassword => StepPassphrase,
            _ => StepStart,
        };
        if (SetupStep == StepStart)
            RefreshSetupState();
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
            // Mai sovrascrivere un wallet esistente: si cerca il primo nome libero.
            var path = AppPaths.DefaultWalletPath(Net);
            for (var n = 2; WalletStore.Exists(path); n++)
                path = Path.Combine(AppPaths.WalletsDir(Net), $"wallet-{n}.wallet.json");
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
            var path = _pendingOpenPath ?? AppPaths.DefaultWalletPath(Net);
            var password = string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput;
            var doc = WalletStore.Load(path, password);
            _pendingOpenPath = null;
            OpenLoaded(doc, WalletLoader.ToAccount(doc), path, password);
        }
        catch (WrongPasswordException)
        {
            StatusMessage = Loc.Tr("msg.wrongpassword");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    /// <summary>Apertura di un file wallet qualunque (menu File → Apri, multi-wallet §8).</summary>
    public void OpenFromPath(string path)
    {
        try
        {
            if (IsWalletOpen)
                CloseWallet();
            var doc = WalletStore.Load(path);
            OpenLoaded(doc, WalletLoader.ToAccount(doc), path, password: null);
        }
        catch (WrongPasswordException)
        {
            // Cifrato: si chiede la password nel passo di apertura del wizard.
            if (IsWalletOpen)
                CloseWallet();
            _pendingOpenPath = path;
            WalletFileExists = true;
            PasswordInput = "";
            SetupStep = StepOpen;
            StatusMessage = $"Il wallet \"{Path.GetFileName(path)}\" è cifrato: inserisci la password e premi Apri.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    /// <summary>Torna al pannello di setup per creare/ripristinare un altro wallet.</summary>
    [RelayCommand]
    private void NewWallet()
    {
        if (IsWalletOpen)
            CloseWallet();
        _pendingOpenPath = null;
        StatusMessage = Loc.Tr("msg.welcome.new");
    }

    private void OpenLoaded(WalletDocument doc, HdAccount account, string path, string? password)
    {
        // La rete del wallet comanda (registry, pin TLS, indirizzi).
        SelectedNetwork = doc.Network;
        _doc = doc;
        _account = account;
        _walletPath = path;
        _password = password;
        MnemonicInput = ConfirmMnemonicInput = PassphraseInput = PasswordInput = "";
        SetupStep = StepStart;

        NetworkInfo = $"{doc.Network} · {doc.ScriptKind} · m/{doc.AccountPath}"
            + (doc.IsWatchOnly ? " · watch-only" : "");
        LoadContacts();
        ApplyCache(doc.Cache);
        IsWalletOpen = true;
        StatusMessage = Loc.Tr("msg.opened");
        // Come Electrum: ci si connette da soli al server selezionato,
        // senza aspettare un click.
        _ = ConnectAndSync();
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
            // Prima della sincronizzazione si mostrano i primi indirizzi derivati.
            Addresses.Clear();
            for (var i = 0; i < 10; i++)
                Addresses.Add(new AddressRow(_loc["addr.receive"], i,
                    _account.GetReceiveAddress(i).ToString(), "—", "—",
                    false,
                    _account.GetPublicKey(false, i).ToHex(),
                    KeyWif(false, i),
                    $"m/{_doc!.AccountPath}/0/{i}"));
            return;
        }
        BalanceText = Fmt(cache.ConfirmedSats);
        // Saldo in attesa: somma delle tx in mempool (può essere negativo per
        // gli invii in uscita non ancora confermati). Non è spendibile finché
        // non conferma: la TransactionFactory usa solo UTXO confermati.
        var pending = cache.History.Where(t => t.Height <= 0).Sum(t => t.DeltaSats);
        UnconfirmedText = pending != 0
            ? $"{Loc.Tr("msg.pending")}: {(pending > 0 ? "+" : "")}{Fmt(pending)} — {Loc.Tr("msg.notspendable")}"
            : "";
        ReceiveAddress = _account.GetReceiveAddress(cache.NextReceiveIndex).ToString();
        History.Clear();
        foreach (var tx in cache.History)
            History.Add(new HistoryRow(
                tx.Height > 0 ? tx.Height.ToString() : "mempool",
                (tx.DeltaSats >= 0 ? "+" : "") + Fmt(tx.DeltaSats, withLabel: false),
                tx.Txid,
                tx.Verified ? "✓ SPV" : "—"));

        Addresses.Clear();
        foreach (var a in cache.Addresses)
            Addresses.Add(new AddressRow(
                a.IsChange ? _loc["addr.change"] : _loc["addr.receive"],
                a.Index,
                a.Address,
                a.BalanceSats > 0 ? Fmt(a.BalanceSats, withLabel: false) : (a.TxCount > 0 ? "0" : "—"),
                a.TxCount > 0 ? a.TxCount.ToString() : "—",
                a.IsChange,
                _account.GetPublicKey(a.IsChange, a.Index).ToHex(),
                KeyWif(a.IsChange, a.Index),
                $"m/{_doc!.AccountPath}/{(a.IsChange ? 1 : 0)}/{a.Index}"));
    }

    // ---------- comandi wallet ----------

    [RelayCommand]
    private async Task ConnectAndSync()
    {
        if (_account is null || _doc is null)
            return;
        if (IsSyncing)
        {
            _resyncRequested = true;
            return;
        }
        IsSyncing = true;
        StatusMessage = "";
        try
        {
            var (host, port) = ParseServer();

            // Se l'endpoint richiesto (host/porta/TLS) è diverso da quello della
            // connessione attiva, la chiudo: l'utente ha cambiato server o ha
            // attivato TLS e si aspetta che la nuova scelta abbia effetto.
            if (_client is { } current &&
                (current.Host != host || current.Port != port || current.UseSsl != UseSsl))
            {
                await DisconnectAsync();
            }

            if (_client is null || !_client.IsConnected)
            {
                ConnectionStatus = $"{Loc.Tr("conn.connectingto")} {host}:{port}…";
                var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(Net));
                _client = await ElectrumClient.ConnectAsync(host, port, UseSsl, pins);
                _client.NotificationReceived += OnServerNotification;
                _client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = Loc.Tr("conn.disconnected");
                });
                IsConnected = true;
                _autoReconnect = true;
                ConnectionStatus = $"{Loc.Tr("conn.connectedto")} {host}:{port}{(UseSsl ? " (TLS)" : "")}";
                // Synchronizer per connessione: conserva la cache di tx e
                // prove verificate, così le risincronizzazioni sono incrementali.
                _synchronizer = new WalletSynchronizer(_account, _client, _doc.GapLimit);
                _synchronizer.Progress += msg => Dispatcher.UIThread.Post(() => StatusMessage = msg);
            }

            // Se durante la sync arrivano notifiche, si ripete subito: nessun
            // aggiornamento del server va perso (modello Electrum).
            do
            {
                _resyncRequested = false;
                var result = await _synchronizer!.SyncOnceAsync();
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
                    Addresses = [.. result.AddressRows],
                };
                WalletStore.Save(_doc, _walletPath!, _password);
                ApplyCache(_doc.Cache);
                StatusMessage = $"{Loc.Tr("msg.synced")}: {Loc.Tr("msg.height")} {result.TipHeight}, " +
                    $"{result.History.Count} {Loc.Tr("msg.synced.detail")}";
            } while (_resyncRequested);
        }
        catch (CertificatePinMismatchException ex)
        {
            IsConnected = false;
            ConnectionStatus = Loc.Tr("conn.certchanged");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            IsConnected = _client?.IsConnected == true;
            ConnectionStatus = IsConnected ? ConnectionStatus : Loc.Tr("conn.error");
            StatusMessage = $"Errore: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>Scopre nuovi server dai peer annunciati dal server connesso (§9).</summary>
    [RelayCommand]
    private async Task DiscoverServers()
    {
        if (_client is null || !_client.IsConnected)
        {
            StatusMessage = Loc.Tr("conn.none") + ".";
            return;
        }
        try
        {
            var added = await Registry.DiscoverAsync(_client);
            var selected = SelectedKnownServer;
            RefreshServers();
            SelectedKnownServer = KnownServers.FirstOrDefault(s => s.Host == selected?.Host)
                ?? KnownServers.FirstOrDefault();
            StatusMessage = added > 0
                ? $"Trovati {added} nuovi server dai peer (totale {KnownServers.Count})."
                : $"Nessun nuovo server annunciato (totale {KnownServers.Count}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore nella scoperta peer: {ex.Message}";
        }
    }

    private void OnServerNotification(string method, System.Text.Json.JsonElement payload)
    {
        // Cambiamento su un nostro indirizzo o nuovo blocco: risincronizza.
        // Se una sync è già in corso, si accoda (il loop in ConnectAndSync la
        // ripete subito dopo): nessuna notifica viene persa.
        if (method is "blockchain.scripthash.subscribe" or "blockchain.headers.subscribe")
            Dispatcher.UIThread.Post(() =>
            {
                if (IsSyncing)
                    _resyncRequested = true;
                else
                    _ = ConnectAndSync();
            });
    }

    [RelayCommand]
    private void ResetCertificates()
    {
        new CertificatePinStore(AppPaths.CertificatePinsPath(Net)).ResetAll();
        StatusMessage = Loc.Tr("msg.certreset");
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
            if (!SendAll && !CoinAmount.TryParseIn(SendAmount, _config.Unit, out amount))
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
                $"fee {Fmt(_pendingSend.Fee.Satoshi)} " +
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

    /// <summary>
    /// Chiude la connessione corrente lasciando il wallet aperto: usata quando
    /// l'utente cambia server o attiva TLS e serve riconnettersi al nuovo
    /// endpoint. Attende la chiusura del socket prima di tornare.
    /// </summary>
    private async Task DisconnectAsync()
    {
        if (_client is { } client)
        {
            _client = null;
            _synchronizer = null;
            try { await client.DisposeAsync(); } catch { /* in chiusura */ }
        }
        IsConnected = false;
    }

    [RelayCommand]
    private void CloseWallet()
    {
        _ = _client?.DisposeAsync().AsTask();
        _client = null;
        _synchronizer = null;
        _autoReconnect = false;
        _resyncRequested = false;
        _doc = null;
        _account = null;
        _lastTransactions = null;
        _pendingSend = null;
        HasPendingSend = false;
        History.Clear();
        Contacts.Clear();
        SelectedContactInList = null;
        SendToContact = null;
        SelectedAddressRow = null;
        IsWalletOpen = false;
        IsConnected = false;
        ConnectionStatus = Loc.Tr("conn.none");
        RefreshSetupState();
    }

    private (string Host, int Port) ParseServer()
    {
        var host = ServerHost.Trim();
        var port = int.TryParse(ServerPort.Trim(), out var p)
            ? p
            : UseSsl ? Profile.DefaultSslPort : Profile.DefaultTcpPort;
        return (host, port);
    }
}
