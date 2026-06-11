using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Tests.Wallet;

public class TransactionFactoryTests
{
    private static readonly ChainProfile Profile = ChainProfiles.Regtest;
    private static readonly Network Net = PalladiumNetworks.Regtest;

    private static HdAccount Account()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return HdAccount.FromMnemonic(mnemonic!, null, ScriptKind.NativeSegwit, Profile);
    }

    /// <summary>Tx fittizia che accredita <paramref name="sats"/> sull'indirizzo receiving/0.</summary>
    private static (List<CachedUtxo>, Dictionary<string, Transaction>) Fund(HdAccount account, long sats)
    {
        var funding = Net.CreateTransaction();
        funding.Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));
        funding.Outputs.Add(Money.Satoshis(sats), account.GetReceiveAddress(0));
        var txid = funding.GetHash().ToString();

        var utxos = new List<CachedUtxo>
        {
            new()
            {
                Txid = txid, Vout = 0, ValueSats = sats,
                Address = account.GetReceiveAddress(0).ToString(),
                IsChange = false, AddressIndex = 0, Height = 100,
            },
        };
        return (utxos, new Dictionary<string, Transaction> { [txid] = funding });
    }

    [Fact]
    public void Una_spesa_firmata_verifica_e_paga_la_fee_attesa()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000_000);
        var destination = BitcoinAddress.Create(account.GetReceiveAddress(5).ToString(), Net);

        var built = new TransactionFactory(account).Build(
            utxos, txs, destination, amountSats: 400_000,
            feeRateSatPerVByte: 2, changeIndex: 0);

        Assert.True(built.Signed);
        // Output: destinatario + change.
        Assert.Equal(2, built.Transaction.Outputs.Count);
        Assert.Contains(built.Transaction.Outputs,
            o => o.ScriptPubKey == destination.ScriptPubKey && o.Value.Satoshi == 400_000);

        // Fee coerente col rate richiesto (±20% per gli arrotondamenti di stima).
        var vsize = built.Transaction.GetVirtualSize();
        var expected = vsize * 2;
        Assert.InRange(built.Fee.Satoshi, expected * 0.8, expected * 1.5);

        // RBF abilitato (§6.6).
        Assert.All(built.Transaction.Inputs, i => Assert.True(i.Sequence < Sequence.Final));
    }

    [Fact]
    public void Invia_tutto_sottrae_la_fee_e_non_produce_change()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 500_000);
        var destination = account.GetReceiveAddress(7);

        var built = new TransactionFactory(account).Build(
            utxos, txs, destination, amountSats: 0,
            feeRateSatPerVByte: 1, changeIndex: 0, sendAll: true);

        var output = Assert.Single(built.Transaction.Outputs);
        Assert.Equal(500_000, output.Value.Satoshi + built.Fee.Satoshi);
    }

    [Fact]
    public void Fondi_insufficienti_danno_un_errore_chiaro()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000);

        Assert.Throws<WalletSpendException>(() => new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 900_000,
            feeRateSatPerVByte: 2, changeIndex: 0));
    }

    [Fact]
    public void Gli_utxo_congelati_sono_esclusi_dalla_spesa()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000_000);
        utxos[0].Frozen = true; // freeze (§6.2)

        Assert.Throws<WalletSpendException>(() => new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0));
    }

    [Fact]
    public void Un_account_watch_only_produce_una_psbt_non_firmata()
    {
        var full = Account();
        var (utxos, txs) = Fund(full, 1_000_000);

        Assert.True(Slip132.TryDecodePublic(full.ToSlip132(), Profile, out var xpub, out var kind));
        var watchOnly = HdAccount.FromAccountXpub(xpub!, kind, Profile);

        var built = new TransactionFactory(watchOnly).Build(
            utxos, txs, full.GetReceiveAddress(5), amountSats: 400_000,
            feeRateSatPerVByte: 2, changeIndex: 0);

        Assert.False(built.Signed);
        // Il flusso air-gapped (§6.5): la PSBT della macchina online si firma
        // offline con le chiavi e si finalizza.
        var psbt = built.Psbt;
        psbt.SignWithKeys(full.GetExtPrivateKey(false, 0));
        psbt.Finalize();
        var tx = psbt.ExtractTransaction();
        Assert.Contains(tx.Outputs, o => o.Value.Satoshi == 400_000);
    }

    [Theory]
    [InlineData("0.00000001", 1L)]
    [InlineData("1", 100_000_000L)]
    [InlineData("0,5", 50_000_000L)]
    [InlineData("21.12345678", 2_112_345_678L)]
    public void Gli_importi_si_parsano_in_satoshi(string text, long expected)
    {
        Assert.True(CoinAmount.TryParseCoins(text, out var sats));
        Assert.Equal(expected, sats);
    }

    [Fact]
    public void Gli_importi_invalidi_vengono_rifiutati()
    {
        Assert.False(CoinAmount.TryParseCoins("abc", out _));
        Assert.False(CoinAmount.TryParseCoins("-1", out _));
    }
}
