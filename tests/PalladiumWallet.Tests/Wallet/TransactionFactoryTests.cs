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

    /// <summary>Fake transaction that credits <paramref name="sats"/> to the receiving/0 address.</summary>
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
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 100);

        Assert.True(built.Signed);
        // Output: recipient + change.
        Assert.Equal(2, built.Transaction.Outputs.Count);
        Assert.Contains(built.Transaction.Outputs,
            o => o.ScriptPubKey == destination.ScriptPubKey && o.Value.Satoshi == 400_000);

        // Fee consistent with the requested rate (±20% for estimation rounding).
        var vsize = built.Transaction.GetVirtualSize();
        var expected = vsize * 2;
        Assert.InRange(built.Fee.Satoshi, expected * 0.8, expected * 1.5);

        // RBF enabled (§6.6).
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
            feeRateSatPerVByte: 1, changeIndex: 0, tipHeight: 100, sendAll: true);

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
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 100));
    }

    [Fact]
    public void Gli_utxo_in_mempool_non_sono_spendibili()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000_000);
        utxos[0].Height = 0; // in mempool: visible in pending balance, not spendable

        var ex = Assert.Throws<WalletSpendException>(() => new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 100));
        Assert.Contains("unconfirmed", ex.Message);
    }

    [Fact]
    public void Gli_utxo_congelati_sono_esclusi_dalla_spesa()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000_000);
        utxos[0].Frozen = true; // frozen (§6.2)

        Assert.Throws<WalletSpendException>(() => new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 100));
    }

    [Fact]
    public void Gli_utxo_coinbase_immaturi_non_sono_spendibili()
    {
        var account = Account();
        var (utxos, txs) = Fund(account, 1_000_000);
        utxos[0].IsCoinbase = true;
        utxos[0].Height = 100;
        // Threshold = COINBASE_MATURITY + 1 = 121 (mirrors the Qt wallet: consensus rule is
        // nSpendHeight - nHeight >= 120, plus one block of safety margin).
        // At height=100, tip=219 → confs = 219-100+1 = 120 < 121 → immature.
        var ex = Assert.Throws<WalletSpendException>(() => new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 219));
        Assert.Contains("coinbase", ex.Message);
        Assert.Contains("120/121", ex.Message);

        // tip=220 → confs = 121 ≥ 121 → mature and spendable.
        var built = new TransactionFactory(account).Build(
            utxos, txs, account.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 220);
        Assert.True(built.Signed);
    }

    [Fact]
    public void Gli_utxo_con_meno_di_minconf_non_sono_spendibili_su_mainnet()
    {
        var mainnetAccount = HdAccount.FromMnemonic(
            Bip39.TryParse("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about", out var m) ? m! : throw new Exception(),
            null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);

        var funding = PalladiumNetworks.Mainnet.CreateTransaction();
        funding.Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));
        funding.Outputs.Add(Money.Satoshis(1_000_000), mainnetAccount.GetReceiveAddress(0));
        var txid = funding.GetHash().ToString();
        var utxos = new List<CachedUtxo>
        {
            new() { Txid = txid, Vout = 0, ValueSats = 1_000_000,
                    Address = mainnetAccount.GetReceiveAddress(0).ToString(),
                    IsChange = false, AddressIndex = 0, Height = 100, IsCoinbase = false },
        };
        var txs = new Dictionary<string, Transaction> { [txid] = funding };

        // 5 confirmations (tipHeight=104): below the mainnet minimum of 6.
        var ex = Assert.Throws<WalletSpendException>(() => new TransactionFactory(mainnetAccount).Build(
            utxos, txs, mainnetAccount.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 104));
        Assert.Contains("5 so far", ex.Message);

        // 6 confirmations (tipHeight=105): exactly at the threshold → spendable.
        var built = new TransactionFactory(mainnetAccount).Build(
            utxos, txs, mainnetAccount.GetReceiveAddress(1), amountSats: 100_000,
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 105);
        Assert.True(built.Signed);
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
            feeRateSatPerVByte: 2, changeIndex: 0, tipHeight: 100);

        Assert.False(built.Signed);
        // Air-gapped flow (§6.5): the online-machine PSBT is signed
        // offline with the keys and then finalised.
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

    // 1.5 PLM = 150_000_000 sat, expressed in each unit (§8).
    [Theory]
    [InlineData("PLM", "1.50000000 PLM", "1.5")]
    [InlineData("mPLM", "1500.00000 mPLM", "1500")]
    [InlineData("µPLM", "1500000.00 µPLM", "1500000")]
    [InlineData("sat", "150000000 sat", "150000000")]
    public void Le_unita_formattano_e_parsano_in_modo_coerente(string unit, string formatted, string input)
    {
        const long sats = 150_000_000;
        Assert.Equal(formatted, CoinAmount.FormatIn(sats, unit));
        Assert.True(CoinAmount.TryParseIn(input, unit, out var parsed));
        Assert.Equal(sats, parsed);
    }

    [Fact]
    public void La_config_globale_fa_roundtrip_su_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plm-config-{Guid.NewGuid()}.json");
        try
        {
            var config = new PalladiumWallet.Core.Storage.AppConfig { Language = "en", Unit = "sat" };
            config.Save(path);
            var loaded = PalladiumWallet.Core.Storage.AppConfig.Load(path);
            Assert.Equal("en", loaded.Language);
            Assert.Equal("sat", loaded.Unit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Una_config_corrotta_torna_ai_default()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plm-config-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{ rotto ");
            var loaded = PalladiumWallet.Core.Storage.AppConfig.Load(path);
            Assert.Equal("en", loaded.Language);
            Assert.Equal("PLM", loaded.Unit);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
