using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Wallet;
using PalladiumWallet.Tests.Net;

namespace PalladiumWallet.Tests.Wallet;

/// <summary>
/// Tests for the transaction detail assembly (§10): fee from resolved inputs,
/// mine/theirs attribution, RBF flag, coinbase, and the degraded paths when the
/// server cannot provide a previous transaction.
/// </summary>
public class TransactionInspectorTests
{
    private static readonly Network Net = PalladiumNetworks.Regtest;

    private static HdAccount Account()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return HdAccount.FromMnemonic(mnemonic!, null, ScriptKind.NativeSegwit, ChainProfiles.Regtest);
    }

    /// <summary>Funding (1M sats to receive/0) + spend (600k external, 390k change/0, 10k fee, RBF).</summary>
    private static (Transaction Funding, Transaction Spend, BitcoinAddress External, HdAccount Account) SpendPair()
    {
        var account = Account();
        var funding = Net.CreateTransaction();
        funding.Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));
        funding.Outputs.Add(Money.Satoshis(1_000_000), account.GetReceiveAddress(0));

        var external = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);
        var spend = Net.CreateTransaction();
        spend.Inputs.Add(new TxIn(new OutPoint(funding, 0)) { Sequence = 0xfffffffd });
        spend.Outputs.Add(Money.Satoshis(600_000), external);
        spend.Outputs.Add(Money.Satoshis(390_000), account.GetChangeAddress(0));
        return (funding, spend, external, account);
    }

    private static async Task<(FakeElectrumServer Server, ElectrumClient Client)> StartAsync(
        params Transaction[] served)
    {
        var byId = served.ToDictionary(t => t.GetHash().ToString());
        var server = new FakeElectrumServer();
        server.Handle("blockchain.transaction.get", p =>
            byId.TryGetValue(p[0].GetString()!, out var tx)
                ? tx.ToHex()
                : throw new FakeElectrumError(-32600, "no such transaction"));
        server.Handle("blockchain.block.header", p =>
        {
            var height = p[0].GetInt32();
            var header = Net.Consensus.ConsensusFactory.CreateBlockHeader();
            header.BlockTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + height);
            header.Bits = new Target(0x1d00ffffu);
            return Convert.ToHexString(header.ToBytes()).ToLowerInvariant();
        });
        var client = await ElectrumClient.ConnectAsync(server.Host, server.Port, useSsl: false);
        return (server, client);
    }

    private static HashSet<string> Owned(HdAccount account) =>
    [
        account.GetReceiveAddress(0).ToString(),
        account.GetChangeAddress(0).ToString(),
    ];

    [Fact]
    public async Task Una_spesa_confermata_riporta_fee_conferme_e_attribuzione_mine_theirs()
    {
        var (funding, spend, external, account) = SpendPair();
        var (server, client) = await StartAsync(funding, spend);
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, spend.GetHash().ToString(), tipHeight: 200, height: 101,
            Owned(account), netSats: -610_000, verified: true);

        Assert.Equal(10_000, details.FeeSats);
        Assert.Equal(1_000_000, details.TotalInSats);
        Assert.Equal(990_000, details.TotalOutSats);
        Assert.Equal(100, details.Confirmations); // 200 − 101 + 1
        Assert.True(details.RbfSignaled);
        Assert.True(details.Verified);
        Assert.False(details.IsCoinbase);
        Assert.False(details.IsIncoming);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_101), details.BlockTime);
        Assert.Equal((double)10_000 / details.VirtualSize, details.FeeRateSatPerVb);

        var input = Assert.Single(details.Inputs);
        Assert.True(input.IsMine);
        Assert.Equal(1_000_000, input.AmountSats);
        Assert.Equal(account.GetReceiveAddress(0).ToString(), input.Address);

        Assert.Equal(2, details.Outputs.Count);
        Assert.Equal(600_000, details.SentToOthersSats);
        Assert.Equal(390_000, details.ReceivedSats);
        Assert.Contains(details.Outputs, o => o.IsMine && o.AmountSats == 390_000);
        Assert.Contains(details.Outputs, o => !o.IsMine && o.AmountSats == 600_000);

        // Outgoing tx: the counterparty is the external recipient.
        Assert.Equal([external.ToString()], details.CounterpartyAddresses);
    }

    [Fact]
    public async Task Una_ricezione_indica_il_mittente_come_controparte()
    {
        var account = Account();
        var senderKey = new Key();
        var senderAddr = senderKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);

        // The sender's coin, then the payment to us spending it.
        var senderFunding = Net.CreateTransaction();
        senderFunding.Inputs.Add(new TxIn(new OutPoint(uint256.One, 1)));
        senderFunding.Outputs.Add(Money.Satoshis(2_000_000), senderAddr);

        var payment = Net.CreateTransaction();
        payment.Inputs.Add(new TxIn(new OutPoint(senderFunding, 0)));
        payment.Outputs.Add(Money.Satoshis(1_500_000), account.GetReceiveAddress(0));

        var (server, client) = await StartAsync(senderFunding, payment);
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, payment.GetHash().ToString(), tipHeight: 200, height: 150,
            Owned(account), netSats: 1_500_000, verified: true);

        Assert.True(details.IsIncoming);
        Assert.Equal([senderAddr.ToString()], details.CounterpartyAddresses);
        Assert.Equal(500_000, details.FeeSats); // 2M in − 1.5M out
    }

    [Fact]
    public async Task Una_coinbase_non_ha_fee_ne_importi_in_ingresso()
    {
        var account = Account();
        var coinbase = Net.CreateTransaction();
        coinbase.Inputs.Add(new TxIn());
        coinbase.Outputs.Add(Money.Satoshis(5_000_000_000), account.GetReceiveAddress(0));

        var (server, client) = await StartAsync(coinbase);
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, coinbase.GetHash().ToString(), tipHeight: 200, height: 190,
            Owned(account), netSats: 5_000_000_000, verified: true);

        Assert.True(details.IsCoinbase);
        Assert.Null(details.FeeSats);
        Assert.Null(details.TotalInSats);
        Assert.Null(details.FeeRateSatPerVb);
        Assert.Equal(11, details.Confirmations);
        var input = Assert.Single(details.Inputs);
        Assert.True(input.IsCoinbase);
        Assert.Null(input.AmountSats);
        // No previous transactions to resolve.
        Assert.Equal(0, server.CallCount("blockchain.transaction.get") - 1); // only the coinbase itself
    }

    [Fact]
    public async Task Se_la_tx_precedente_non_e_recuperabile_la_fee_resta_ignota()
    {
        var (funding, spend, _, account) = SpendPair();
        // The server only knows the spend: the funding lookup fails.
        var (server, client) = await StartAsync(spend);
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, spend.GetHash().ToString(), tipHeight: 200, height: 101,
            Owned(account), netSats: -610_000, verified: false);

        Assert.Null(details.FeeSats);
        Assert.Null(details.TotalInSats);
        var input = Assert.Single(details.Inputs);
        Assert.Null(input.AmountSats);
        Assert.False(input.IsMine); // unresolvable → not attributable
        Assert.Equal(funding.GetHash().ToString(), input.PrevTxid);
    }

    [Fact]
    public async Task Una_tx_in_mempool_non_ha_conferme_ne_timestamp()
    {
        var (funding, spend, _, account) = SpendPair();
        var (server, client) = await StartAsync(funding, spend);
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, spend.GetHash().ToString(), tipHeight: 200, height: 0,
            Owned(account), netSats: -610_000, verified: false);

        Assert.Equal(0, details.Confirmations);
        Assert.Null(details.BlockTime);
        Assert.Equal(0, server.CallCount("blockchain.block.header"));
    }

    [Fact]
    public async Task La_cache_delle_tx_evita_le_richieste_al_server()
    {
        var (funding, spend, _, account) = SpendPair();
        var cache = new Dictionary<string, Transaction>
        {
            [funding.GetHash().ToString()] = funding,
            [spend.GetHash().ToString()] = spend,
        };
        var (server, client) = await StartAsync();
        await using var _ = server; await using var __ = client;

        var details = await TransactionInspector.FetchAsync(
            client, Net, spend.GetHash().ToString(), tipHeight: 200, height: 0,
            Owned(account), netSats: -610_000, verified: false, cache);

        Assert.Equal(10_000, details.FeeSats);
        Assert.Equal(0, server.CallCount("blockchain.transaction.get"));
    }
}
