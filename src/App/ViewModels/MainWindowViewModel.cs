using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
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
    Loc Loc,
    string Address, string DerivPath, string PubKey, string PrivKey)
{
    public bool HasPrivKey => !string.IsNullOrEmpty(PrivKey);
}

/// <summary>Contatto in rubrica: nome + indirizzo blockchain.</summary>
public sealed record ContactEntry(string Name, string Address);

/// <summary>Voce della lista di scelta wallet: nome file + percorso completo.</summary>
public sealed record WalletFileEntry(string Name, string Path);

/// <summary>
/// ViewModel unico dell'applicazione (wizard + dashboard). Suddiviso in file
/// partial per area: Wizard, Settings, Sync, Send, Contacts, Receive.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // ---- stato sessione wallet ----
    private WalletDocument? _doc;
    private HdAccount? _account;
    private string? _walletPath;
    private string? _password;
    private WalletLock? _walletLock;

    // ---- rete ----
    private ElectrumClient? _client;
    private WalletSynchronizer? _synchronizer;
    private IReadOnlyDictionary<string, Transaction>? _lastTransactions;
    private bool _resyncRequested;

    // ---- invio ----
    private BuiltTransaction? _pendingSend;

    // ---- dettaglio transazione ----
    private CancellationTokenSource? _txDetailsCts;

    // ---- configurazione e localizzazione ----
    private AppConfig _config = AppConfig.Load();
    private Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    // ---- wizard ----
    private string? _pendingOpenPath;
    private bool _isRestoreFlow;

    // ---- keep-alive ----
    private bool _autoReconnect;
    private readonly DispatcherTimer _keepAliveTimer;

    // ---- server UI sync ----
    private bool _syncingServerFields;

    // ---- proprietà di app ----

    public static string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "";

    public string WindowTitle => $"Palladium Wallet {AppVersion}";

    /// <summary>true su desktop; false su Android/iOS — nasconde le funzioni legate al filesystem libero.</summary>
    public bool IsDesktop => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    public bool IsMobile => !IsDesktop;

    public string UnitLabel => _config.Unit;
    public AppConfig CurrentConfig => _config;

    // ---- stato pannelli ----

    public string[] Networks { get; } = ["mainnet", "testnet", "regtest"];

    [ObservableProperty]
    private string selectedNetwork = "mainnet";

    partial void OnSelectedNetworkChanged(string value) => RefreshSetupState();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupVisible))]
    private bool isWalletOpen;

    public bool IsSetupVisible => !IsWalletOpen;

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private int selectedTabIndex;

    [RelayCommand]
    private void SelectTab(string index) => SelectedTabIndex = int.Parse(index);

    // ---- collections per la dashboard ----

    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<AddressRow> Addresses { get; } = [];

    // ---- helpers ----

    private NetKind Net => System.Enum.Parse<NetKind>(SelectedNetwork, ignoreCase: true);
    private ChainProfile Profile => ChainProfiles.For(Net);
    private ServerRegistry Registry => new(Profile, AppPaths.ServersPath(Net));

    private string Fmt(long sats, bool withLabel = true) =>
        CoinAmount.FormatIn(sats, _config.Unit, withLabel);

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

    // ---- costruttore ----

    public MainWindowViewModel()
    {
        _loc = Loc.SwitchTo(_config.Language);
        if (!AppPaths.IsDataLocationConfigured())
            SetupStep = StepDataLocation;
        else
            RefreshSetupState();
        _keepAliveTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(20) };
        _keepAliveTimer.Tick += async (_, _) => await KeepAliveTickAsync();
        _keepAliveTimer.Start();
    }

    private async System.Threading.Tasks.Task KeepAliveTickAsync()
    {
        if (IsSyncing)
            return;
        if (_client is { IsConnected: true })
        {
            try { await _client.PingAsync(); }
            catch { }
        }
        else if (_autoReconnect)
        {
            ConnectionStatus = Loc.Tr("conn.reconnecting");
            await ConnectAndSync();
        }
    }

    // ---- ciclo di vita wallet ----

    [RelayCommand]
    private void CloseWallet()
    {
        _walletLock?.Dispose();
        _walletLock = null;
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
}
