using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
    // ---- server and connection ----

    [ObservableProperty]
    private string serverHost = "";

    [ObservableProperty]
    private string serverPort = "";

    [ObservableProperty]
    private bool useSsl = true;

    [ObservableProperty]
    private bool isServerSettingsOpen;

    [RelayCommand]
    private async Task OpenServerSettings()
    {
        IsSettingsOpen = false;
        RefreshServers();
        IsServerSettingsOpen = true;
        if (_client is { IsConnected: true })
            await DiscoverServers();
    }

    [RelayCommand]
    private void CloseServerSettings() => IsServerSettingsOpen = false;

    [ObservableProperty]
    private string connectionStatus = Loc.Tr("conn.none");

    [ObservableProperty]
    private string connectionStatusShort = Loc.Tr("conn.none");

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private bool isSyncing;

    private CancellationTokenSource _syncCts = new();

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

        if (_config.LastServerHost is { Length: > 0 } savedHost && _config.LastServerPort is { } savedPort)
        {
            _syncingServerFields = true;
            UseSsl = _config.LastServerUseSsl;
            ServerHost = savedHost;
            ServerPort = savedPort.ToString();
            SelectedKnownServer = KnownServers.FirstOrDefault(s => s.Host == savedHost);
            _syncingServerFields = false;
        }
        else
        {
            SelectedKnownServer = KnownServers.FirstOrDefault();
            if (SelectedKnownServer is null)
            {
                ServerHost = "127.0.0.1";
                ServerPort = (UseSsl ? Profile.DefaultSslPort : Profile.DefaultTcpPort).ToString();
            }
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
    /// Returns servers to try in order: first the one selected in the UI
    /// (or manually typed), then the remaining KnownServers, with duplicates skipped.
    /// </summary>
    private IEnumerable<(string Host, int Port)> BuildServerCandidates(string selectedHost, int selectedPort)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 1. Current server (selected or manually typed).
        if (!string.IsNullOrWhiteSpace(selectedHost))
        {
            var key = $"{selectedHost}:{selectedPort}";
            if (seen.Add(key))
                yield return (selectedHost, selectedPort);
        }
        // 2. Other known servers, in list order.
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
            var (newHost, newPort) = ParseServer();
            bool serverChanged = _client is null
                || _client.Host != newHost
                || _client.Port != newPort
                || _client.UseSsl != UseSsl;
            if (serverChanged)
                _syncCts.Cancel();
            else
                _resyncRequested = true;
            return;
        }

        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;
        IsSyncing = true;
        StatusMessage = "";
        bool cancelled = false;
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
                // Persist caches before destroying the synchronizer: the new
                // synchronizer will use a different client and must be recreated,
                // but already-downloaded data is preserved via _doc.Cache.
                PersistPartialTxCache();
                _synchronizer = null;

                // Try all known servers in order; starts with the selected one
                // and walks the list until the first that responds.
                var candidates = BuildServerCandidates(host, port);
                Exception? lastError = null;
                foreach (var (h, p) in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    ConnectionStatus = $"{Loc.Tr("conn.connectingto")} {h}:{p}…";
                    ConnectionStatusShort = Loc.Tr("conn.connectingto") + "…";
                    try
                    {
                        var pins = new CertificatePinStore(AppPaths.CertificatePinsPath(Net));
                        _client = await ElectrumClient.ConnectAsync(h, p, UseSsl, pins, ct);
                        _client.NotificationReceived += OnServerNotification;
                        _client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
                        {
                            IsConnected = false;
                            ConnectionStatus = Loc.Tr("conn.none");
                            ConnectionStatusShort = Loc.Tr("conn.none");
                        });
                        IsConnected = true;
                        _autoReconnect = true;
                        ConnectionStatus = $"{Loc.Tr("conn.connectedto")} {h}:{p}";
                        ConnectionStatusShort = Loc.Tr("conn.connectedto");
                        // Update the UI to reflect the server actually connected.
                        _syncingServerFields = true;
                        ServerHost = h;
                        ServerPort = p.ToString();
                        SelectedKnownServer = KnownServers.FirstOrDefault(s => s.Host == h)
                            ?? SelectedKnownServer;
                        _syncingServerFields = false;
                        // Persist the last-used server so it is restored on next launch.
                        _config.LastServerHost = h;
                        _config.LastServerPort = p;
                        _config.LastServerUseSsl = UseSsl;
                        _config.Save();
                        lastError = null;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
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
                // Reload from disk cache: avoids re-downloading already known transactions
                // (essential for wallets with thousands of historical txs — prevents -101).
                var net = PalladiumNetworks.For(_account.Profile.Kind);
                _synchronizer.PreloadCaches(
                    _doc.Cache?.RawTxHex ?? [],
                    _doc.Cache?.VerifiedAt ?? [],
                    _doc.Cache?.BlockHeaders,
                    _doc.Cache?.NextReceiveIndex ?? 0,
                    _doc.Cache?.NextChangeIndex ?? 0,
                    net);
                _synchronizer.Progress += msg => Dispatcher.UIThread.Post(() => StatusMessage = msg);
            }

            do
            {
                _resyncRequested = false;
                var result = await _synchronizer.SyncOnceAsync(ct);
                _lastTransactions = result.Transactions;

                var (rawHex, verifiedAt, blockHeaders) = _synchronizer.ExportCaches(
                    PalladiumNetworks.For(_account.Profile.Kind));
                _doc.Cache = new SyncCache
                {
                    TipHeight = result.TipHeight,
                    ConfirmedSats = result.ConfirmedSats,
                    UnconfirmedSats = result.UnconfirmedSats,
                    ImmatureSats = result.ImmatureSats,
                    NextReceiveIndex = result.NextReceiveIndex,
                    NextChangeIndex = result.NextChangeIndex,
                    History = [.. result.History],
                    Utxos = [.. result.Utxos],
                    Addresses = [.. result.AddressRows],
                    RawTxHex = rawHex,
                    VerifiedAt = verifiedAt,
                    BlockHeaders = blockHeaders,
                };
                WalletStore.Save(_doc, _walletPath!, _password);
                ApplyCache(_doc.Cache);
                _syncFailed = false;
                StatusMessage = $"{Loc.Tr("msg.synced")}: {Loc.Tr("msg.height")} {result.TipHeight}, " +
                    $"{result.History.Count} {Loc.Tr("msg.synced.detail")}";
            } while (_resyncRequested && !ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation due to server change request — not an error.
            cancelled = true;
            IsConnected = false;
            ConnectionStatus = Loc.Tr("conn.none");
            ConnectionStatusShort = Loc.Tr("conn.none");
            StatusMessage = "";
        }
        catch (CertificatePinMismatchException ex)
        {
            IsConnected = false;
            ConnectionStatus = Loc.Tr("conn.none");
            ConnectionStatusShort = Loc.Tr("conn.none");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            IsConnected = _client?.IsConnected == true;
            ConnectionStatus = IsConnected ? ConnectionStatus : Loc.Tr("conn.none");
            ConnectionStatusShort = IsConnected ? Loc.Tr("conn.connectedto") : Loc.Tr("conn.none");
            StatusMessage = $"Errore: {ex.Message}";
            if (_account is not null)
            {
                _syncFailed = true;
                // Persist already-downloaded transactions: on retry the synchronizer
                // resumes from here instead of starting from scratch (e.g. after -101).
                PersistPartialTxCache();
            }
        }
        finally
        {
            IsSyncing = false;
        }

        // If cancelled due to a server change, restart immediately with the new server.
        if (cancelled)
            _ = ConnectAndSync();
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
    /// Persists the transactions and Merkle proofs already accumulated by the synchronizer
    /// into SyncCache, even if sync is not yet complete. Allows the next retry
    /// (or app restart) to resume without re-downloading from scratch.
    /// </summary>
    private void PersistPartialTxCache()
    {
        if (_synchronizer is null || _doc is null || _walletPath is null || _account is null)
            return;
        try
        {
            var net = PalladiumNetworks.For(_account.Profile.Kind);
            var (rawHex, verifiedAt, blockHeaders) = _synchronizer.ExportCaches(net);
            if (rawHex.Count == 0 && verifiedAt.Count == 0)
                return;
            (_doc.Cache ??= new SyncCache()).RawTxHex = rawHex;
            _doc.Cache.VerifiedAt = verifiedAt;
            _doc.Cache.BlockHeaders = blockHeaders;
            WalletStore.Save(_doc, _walletPath, _password);
        }
        catch { /* non-fatal: the next full save will recover */ }
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
