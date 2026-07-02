using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBitcoin;
using PalladiumWallet.App.Services;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.App.ViewModels;

public partial class MainWindowViewModel
{
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

    [RelayCommand]
    private async Task ScanQr()
    {
        if (PlatformServices.ScanQrAsync is not { } scan) return;
        var raw = await scan();
        if (string.IsNullOrWhiteSpace(raw)) return;
        // Handle URIs like "palladium:ADDRESS?amount=X" — extract address only
        var address = raw.Contains(':') ? raw.Split(':')[1] : raw;
        if (address.Contains('?')) address = address.Split('?')[0];
        SendTo = address.Trim();
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
                _doc.Cache.NextChangeIndex, _doc.Cache.TipHeight, SendAll);

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
}
