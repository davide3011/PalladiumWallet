using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalladiumWallet.App.Localization;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.App.ViewModels;

public partial class MainWindowViewModel
{
    // ---- server e connessione ----

    [ObservableProperty]
    private string serverHost = "";

    [ObservableProperty]
    private string serverPort = "";

    [ObservableProperty]
    private bool useSsl = true;

    [ObservableProperty]
    private bool isServerSettingsOpen;

    [RelayCommand]
    private void OpenServerSettings()
    {
        IsSettingsOpen = false;
        IsServerSettingsOpen = true;
    }

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

    private (string Host, int Port) ParseServer()
    {
        var host = ServerHost.Trim();
        var port = int.TryParse(ServerPort.Trim(), out var p)
            ? p
            : UseSsl ? Profile.DefaultSslPort : Profile.DefaultTcpPort;
        return (host, port);
    }

    /// <summary>
    /// Restituisce i server da provare in ordine: prima quello selezionato nella UI
    /// (o digitato manualmente), poi gli altri in KnownServers, evitando duplicati.
    /// </summary>
    private IEnumerable<(string Host, int Port)> BuildServerCandidates(string selectedHost, int selectedPort)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 1. Server corrente (selezionato o digitato manualmente).
        if (!string.IsNullOrWhiteSpace(selectedHost))
        {
            var key = $"{selectedHost}:{selectedPort}";
            if (seen.Add(key))
                yield return (selectedHost, selectedPort);
        }
        // 2. Altri server noti, in ordine di lista.
        foreach (var s in KnownServers)
        {
            var p = s.PortFor(UseSsl);
            var key = $"{s.Host}:{p}";
            if (seen.Add(key))
                yield return (s.Host, p);
        }
    }

    [RelayCommand]
    private async Task ConnectAndSync()
    {
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

            if (_client is { } current &&
                (current.Host != host || current.Port != port || current.UseSsl != UseSsl))
            {
                await DisconnectAsync();
            }

            if (_client is null || !_client.IsConnected)
            {
                // Salva le cache prima di distruggere il synchronizer: il nuovo
                // synchronizer userà un client diverso e deve essere ricreato,
                // ma i dati già scaricati vengono preservati via _doc.Cache.
                PersistPartialTxCache();
                _synchronizer = null;

                // Prova tutti i server noti in ordine; parte da quello selezionato
                // e scorre la lista fino al primo che risponde.
                var candidates = BuildServerCandidates(host, port);
                Exception? lastError = null;
                foreach (var (h, p) in candidates)
                {
                    ConnectionStatus = $"{Loc.Tr("conn.connectingto")} {h}:{p}…";
                    try
                    {
                        var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(Net));
                        _client = await ElectrumClient.ConnectAsync(h, p, UseSsl, pins);
                        _client.NotificationReceived += OnServerNotification;
                        _client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
                        {
                            IsConnected = false;
                            ConnectionStatus = Loc.Tr("conn.none");
                        });
                        IsConnected = true;
                        _autoReconnect = true;
                        ConnectionStatus = Loc.Tr("conn.connectedto");
                        // Aggiorna la UI per riflettere il server effettivamente connesso.
                        _syncingServerFields = true;
                        ServerHost = h;
                        ServerPort = p.ToString();
                        SelectedKnownServer = KnownServers.FirstOrDefault(s => s.Host == h)
                            ?? SelectedKnownServer;
                        _syncingServerFields = false;
                        lastError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        _client = null;
                    }
                }
                if (lastError is not null)
                    throw lastError;
            }

            if (_account is null || _doc is null)
                return;

            if (_synchronizer is null)
            {
                _synchronizer = new WalletSynchronizer(_account, _client!, _doc.GapLimit);
                // Ricarica dalla cache su disco: evita di riscaricale le tx già note
                // (fondamentale per wallet con migliaia di tx storiche — previene -101).
                var net = PalladiumNetworks.For(_account.Profile.Kind);
                _synchronizer.PreloadCaches(
                    _doc.Cache?.RawTxHex ?? [],
                    _doc.Cache?.VerifiedAt ?? [],
                    net);
                _synchronizer.Progress += msg => Dispatcher.UIThread.Post(() => StatusMessage = msg);
            }

            do
            {
                _resyncRequested = false;
                var result = await _synchronizer.SyncOnceAsync();
                _lastTransactions = result.Transactions;

                var (rawHex, verifiedAt) = _synchronizer.ExportCaches(
                    PalladiumNetworks.For(_account.Profile.Kind));
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
                    RawTxHex = rawHex,
                    VerifiedAt = verifiedAt,
                };
                WalletStore.Save(_doc, _walletPath!, _password);
                ApplyCache(_doc.Cache);
                _syncFailed = false;
                StatusMessage = $"{Loc.Tr("msg.synced")}: {Loc.Tr("msg.height")} {result.TipHeight}, " +
                    $"{result.History.Count} {Loc.Tr("msg.synced.detail")}";
            } while (_resyncRequested);
        }
        catch (CertificatePinMismatchException ex)
        {
            IsConnected = false;
            ConnectionStatus = Loc.Tr("conn.none");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            IsConnected = _client?.IsConnected == true;
            ConnectionStatus = IsConnected ? ConnectionStatus : Loc.Tr("conn.none");
            StatusMessage = $"Errore: {ex.Message}";
            if (_account is not null)
            {
                _syncFailed = true;
                // Salva le tx già scaricate: al retry il synchronizer riparte
                // da qui invece di ricominciare da zero (es. dopo -101).
                PersistPartialTxCache();
            }
        }
        finally
        {
            IsSyncing = false;
        }
    }

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

    /// <summary>
    /// Salva nel SyncCache le tx e le prove di Merkle già accumulate dal synchronizer,
    /// anche se la sync non è ancora completa. Consente al retry successivo
    /// (o al riavvio dell'app) di riprendere senza riscaricale da zero.
    /// </summary>
    private void PersistPartialTxCache()
    {
        if (_synchronizer is null || _doc is null || _walletPath is null || _account is null)
            return;
        try
        {
            var net = PalladiumNetworks.For(_account.Profile.Kind);
            var (rawHex, verifiedAt) = _synchronizer.ExportCaches(net);
            if (rawHex.Count == 0 && verifiedAt.Count == 0)
                return;
            (_doc.Cache ??= new SyncCache()).RawTxHex = rawHex;
            _doc.Cache.VerifiedAt = verifiedAt;
            WalletStore.Save(_doc, _walletPath, _password);
        }
        catch { /* non fatale: il prossimo salvataggio completo recupererà */ }
    }

    private async Task DisconnectAsync()
    {
        if (_client is { } client)
        {
            _client = null;
            _synchronizer = null;
            try { await client.DisposeAsync(); } catch { }
        }
        IsConnected = false;
    }
}
