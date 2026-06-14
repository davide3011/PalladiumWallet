using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalladiumWallet.App.Localization;
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
                ConnectionStatus = $"{Loc.Tr("conn.connectingto")} {host}:{port}…";
                var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(Net));
                _client = await ElectrumClient.ConnectAsync(host, port, UseSsl, pins);
                _client.NotificationReceived += OnServerNotification;
                _client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
                {
                    IsConnected = false;
                    ConnectionStatus = Loc.Tr("conn.none");
                });
                IsConnected = true;
                _autoReconnect = true;
                ConnectionStatus = Loc.Tr("conn.connectedto");
            }

            if (_account is null || _doc is null)
                return;

            if (_synchronizer is null)
            {
                _synchronizer = new WalletSynchronizer(_account, _client, _doc.GapLimit);
                _synchronizer.Progress += msg => Dispatcher.UIThread.Post(() => StatusMessage = msg);
            }

            do
            {
                _resyncRequested = false;
                var result = await _synchronizer.SyncOnceAsync();
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
            ConnectionStatus = Loc.Tr("conn.none");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            IsConnected = _client?.IsConnected == true;
            ConnectionStatus = IsConnected ? ConnectionStatus : Loc.Tr("conn.none");
            StatusMessage = $"Errore: {ex.Message}";
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
