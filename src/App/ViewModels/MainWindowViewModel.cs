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

/// <summary>Transaction history row for the view.</summary>
public sealed record HistoryRow(string Conferma, string Importo, string Txid, bool Verified = true);

/// <summary>Address view row with pre-computed keys and derivation path.</summary>
public sealed record AddressRow(
    string Tipo, int Indice, string Indirizzo, string Saldo, string NumTx,
    bool IsChange = false, string PubKey = "", string PrivKey = "", string DerivPath = "")
{
    public bool HasPrivKey => !string.IsNullOrEmpty(PrivKey);
}

/// <summary>Full address data passed to the address detail overlay.</summary>
public sealed record AddressInfo(
    Loc Loc,
    string Address, string DerivPath, string PubKey, string PrivKey)
{
    public bool HasPrivKey => !string.IsNullOrEmpty(PrivKey);
}

/// <summary>Address book contact: name + blockchain address.</summary>
public sealed record ContactEntry(string Name, string Address);

/// <summary>Wallet list entry: display name + full file path.</summary>
public sealed record WalletFileEntry(string Name, string Path);

/// <summary>
/// Single application ViewModel (wizard + dashboard). Split into partial files
/// by area: Wizard, Settings, Sync, Send, Contacts, Receive.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // ---- wallet session state ----
    private WalletDocument? _doc;
    private IWalletAccount? _account;
    private string? _walletPath;
    private string? _password;
    private WalletLock? _walletLock;

    // ---- network ----
    private ElectrumClient? _client;
    private WalletSynchronizer? _synchronizer;
    private IReadOnlyDictionary<string, Transaction>? _lastTransactions;
    private bool _resyncRequested;

    // ---- send ----
    private BuiltTransaction? _pendingSend;

    // ---- transaction detail ----
    private CancellationTokenSource? _txDetailsCts;

    // ---- configuration and localisation ----
    private AppConfig _config = AppConfig.Load();
    private Loc _loc = Loc.Instance;
    public Loc Loc => _loc;

    // ---- wizard ----
    private string? _pendingOpenPath;

    // ---- keep-alive ----
    private bool _autoReconnect;
    private bool _syncFailed;
    private readonly DispatcherTimer _keepAliveTimer;

    // ---- server UI sync ----
    private bool _syncingServerFields;

    // ---- app properties ----

    public static string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "";

    public string WindowTitle => $"Palladium Wallet {AppVersion}";

    /// <summary>True on desktop; false on Android/iOS — hides filesystem-dependent features.</summary>
    public bool IsDesktop => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    public bool IsMobile => !IsDesktop;

    // Tab bar sizing: compact on mobile so all 5 tabs fit in one row.
    public double TabIconSize  => IsMobile ? 20 : 24;
    public double TabFontSize  => IsMobile ? 10 : 13;
    public double TabSpacing   => IsMobile ? 4  : 4;

    public string UnitLabel => _config.Unit;
    public AppConfig CurrentConfig => _config;

    // ---- panel state ----

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

    // ---- dashboard collections ----

    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<AddressRow> Addresses { get; } = [];

    // ---- helpers ----

    private NetKind Net => NetKind.Mainnet;
    private ChainProfile Profile => ChainProfiles.For(Net);
    private ServerRegistry Registry => new(Profile, AppPaths.ServersPath(Net));

    private string Fmt(long sats, bool withLabel = true) =>
        CoinAmount.FormatIn(sats, _config.Unit, withLabel);

    private string KeyWif(bool isChange, int index)
    {
        if (_account is null or { IsWatchOnly: true }) return "";
        try
        {
            return _account.GetPrivateKey(isChange, index)
                ?.GetWif(PalladiumNetworks.For(Net)).ToString() ?? "";
        }
        catch { return ""; }
    }

    // ---- constructor ----

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
        _ = CheckForUpdatesAsync();
    }

    private async System.Threading.Tasks.Task KeepAliveTickAsync()
    {
        if (IsSyncing)
            return;
        if (_client is { IsConnected: true } client)
        {
            // If the wallet is open and the last sync failed, retry automatically.
            if (_syncFailed && _account is not null)
            {
                await ConnectAndSync();
                return;
            }
            try
            {
                await client.PingAsync();
            }
            catch
            {
                // TcpClient.Connected only reflects the last known socket state, so a
                // connection killed silently while the app was suspended (e.g. Android
                // Doze after screen lock) still reports IsConnected == true. A failed
                // ping is the only reliable signal here: tear the dead client down so
                // the next tick reconnects instead of retrying forever on a dead socket.
                await DisconnectAsync();
                if (_autoReconnect)
                {
                    ConnectionStatus = Loc.Tr("conn.reconnecting");
                    await ConnectAndSync();
                }
            }
        }
        else if (_autoReconnect)
        {
            ConnectionStatus = Loc.Tr("conn.reconnecting");
            await ConnectAndSync();
        }
    }

    /// <summary>Forces an immediate connection health check, bypassing the 20s timer.
    /// Called when the app resumes from background/lock screen, since the socket may
    /// have died silently while suspended and the UI would otherwise show a stale
    /// "connected" state until the next scheduled tick (or forever, if it never fires
    /// during Doze).</summary>
    public async System.Threading.Tasks.Task CheckConnectionOnResumeAsync()
    {
        if (IsSyncing) return;
        await KeepAliveTickAsync();
    }

    // ---- wallet lifecycle ----

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
        _syncFailed = false;
        _doc = null;
        _account = null;
        _lastTransactions = null;
        _pendingSend = null;
        HasPendingSend = false;
        PendingPsbtBase64 = "";
        OnPropertyChanged(nameof(IsWatchOnlyAccount));
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
