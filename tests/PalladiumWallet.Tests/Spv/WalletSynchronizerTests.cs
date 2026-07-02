using System.Text.Json;
using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;
using PalladiumWallet.Tests.Net;

namespace PalladiumWallet.Tests.Spv;

/// <summary>
/// End-to-end tests of the SPV synchroniser against the in-process fake server:
/// gap-limit scanning, UTXO/history reconstruction, Merkle verification (the
/// path that must reject a lying server), busy retry, and the disk cache.
/// Every block in these scenarios contains a single transaction, so the Merkle
/// root is the txid itself and the branch is empty (the tree math has its own
/// dedicated tests in <see cref="MerkleProofTests"/>).
/// </summary>
public class WalletSynchronizerTests
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

    /// <summary>
    /// Server-side chain state: transactions, per-scripthash histories and
    /// single-transaction block headers, wired onto a FakeElectrumServer.
    /// </summary>
    private sealed class Scenario
    {
        public int TipHeight = 200;
        public readonly Dictionary<string, Transaction> Txs = [];
        public readonly Dictionary<string, List<(string Txid, int Height)>> History = [];
        public readonly Dictionary<int, string> Headers = [];

        /// <summary>Creates a tx paying <paramref name="sats"/> to <paramref name="to"/> and registers it.</summary>
        public Transaction Pay(BitcoinAddress to, long sats, int height, OutPoint? from = null,
            bool coinbase = false, BitcoinAddress? changeTo = null, long changeSats = 0)
        {
            var tx = Net.CreateTransaction();
            tx.Inputs.Add(coinbase ? new TxIn() : new TxIn(from ?? new OutPoint(uint256.One, 0)));
            tx.Outputs.Add(Money.Satoshis(sats), to);
            if (changeTo is not null)
                tx.Outputs.Add(Money.Satoshis(changeSats), changeTo);
            Register(tx, height, to, changeTo);
            return tx;
        }

        public void Register(Transaction tx, int height, params BitcoinAddress?[] touchedAddresses)
        {
            var txid = tx.GetHash().ToString();
            Txs[txid] = tx;
            foreach (var addr in touchedAddresses)
            {
                if (addr is null) continue;
                var sh = Scripthash.FromAddress(addr);
                if (!History.TryGetValue(sh, out var list))
                    History[sh] = list = [];
                if (!list.Contains((txid, height)))
                    list.Add((txid, height));
            }
            if (height > 0 && !Headers.ContainsKey(height))
                Headers[height] = SingleTxHeaderHex(tx.GetHash(), height);
        }

        /// <summary>80-byte header of a block whose only transaction is <paramref name="txid"/>.</summary>
        public static string SingleTxHeaderHex(uint256 txid, int height, uint256? merkleRoot = null)
        {
            var header = Net.Consensus.ConsensusFactory.CreateBlockHeader();
            header.HashPrevBlock = uint256.Zero;
            header.HashMerkleRoot = merkleRoot ?? txid;
            header.BlockTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + height);
            header.Bits = new Target(0x1d00ffffu);
            header.Nonce = (uint)height;
            return Convert.ToHexString(header.ToBytes()).ToLowerInvariant();
        }

        public void WireTo(FakeElectrumServer server)
        {
            server.Handle("blockchain.headers.subscribe",
                _ => new { height = TipHeight, hex = SingleTxHeaderHex(uint256.One, TipHeight) });
            server.Handle("blockchain.scripthash.subscribe", _ => null);
            server.Handle("blockchain.scripthash.get_history", p =>
                (History.GetValueOrDefault(p[0].GetString()!) ?? [])
                    .Select(h => new { tx_hash = h.Txid, height = h.Height }));
            server.Handle("blockchain.transaction.get", p =>
                Txs.TryGetValue(p[0].GetString()!, out var tx)
                    ? tx.ToHex()
                    : throw new FakeElectrumError(-32600, "no such transaction"));
            server.Handle("blockchain.transaction.get_merkle", p =>
                new { block_height = p[1].GetInt32(), pos = 0, merkle = Array.Empty<string>() });
            server.Handle("blockchain.block.header", p =>
                Headers.TryGetValue(p[0].GetInt32(), out var hex)
                    ? hex
                    : throw new FakeElectrumError(-32600, "no such block"));
        }
    }

    private static async Task<(FakeElectrumServer Server, ElectrumClient Client)> StartAsync(Scenario scenario)
    {
        var server = new FakeElectrumServer();
        scenario.WireTo(server);
        var client = await ElectrumClient.ConnectAsync(server.Host, server.Port, useSsl: false);
        return (server, client);
    }

    // ---- scansione ----

    [Fact]
    public async Task Wallet_vuoto_scansiona_il_gap_limit_e_riporta_saldo_zero()
    {
        var scenario = new Scenario();
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(Account(), client).SyncOnceAsync();

        Assert.Equal(200, result.TipHeight);
        Assert.Equal(0, result.ConfirmedSats);
        Assert.Equal(0, result.UnconfirmedSats);
        Assert.Equal(0, result.NextReceiveIndex);
        Assert.Equal(0, result.NextChangeIndex);
        // Default gap limit 20 on both chains.
        Assert.Equal(40, result.Addresses.Count);
        Assert.Equal(40, server.CallCount("blockchain.scripthash.get_history"));
        Assert.Empty(result.Utxos);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task La_scoperta_continua_oltre_il_primo_batch_quando_un_indirizzo_e_usato()
    {
        var account = Account();
        var scenario = new Scenario();
        scenario.Pay(account.GetReceiveAddress(3), 50_000, height: 100);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client, gapLimit: 5).SyncOnceAsync();

        // Index 3 used → the window slides: 5 more empty addresses (5..9) close the scan.
        Assert.Equal(4, result.NextReceiveIndex);
        Assert.Equal(10, result.Addresses.Count(a => !a.IsChange));
        Assert.Equal(5, result.Addresses.Count(a => a.IsChange));
        Assert.Equal(50_000, result.ConfirmedSats);
    }

    // ---- ricostruzione UTXO e storico ----

    [Fact]
    public async Task Una_ricezione_confermata_produce_utxo_saldo_e_storico_verificato()
    {
        var account = Account();
        var scenario = new Scenario();
        var funding = scenario.Pay(account.GetReceiveAddress(0), 1_000_000, height: 100);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();

        Assert.Equal(1_000_000, result.ConfirmedSats);
        Assert.Equal(0, result.ImmatureSats); // regtest: 1 conferma minima, ne ha 101
        Assert.Equal(1, result.NextReceiveIndex);

        var utxo = Assert.Single(result.Utxos);
        Assert.Equal(funding.GetHash().ToString(), utxo.Txid);
        Assert.Equal(100, utxo.Height);
        Assert.False(utxo.IsChange);

        var entry = Assert.Single(result.History);
        Assert.Equal(1_000_000, entry.DeltaSats);
        Assert.True(entry.Verified); // Merkle proof checked against the header

        var row = result.AddressRows.Single(r => r.Address == account.GetReceiveAddress(0).ToString());
        Assert.Equal(1_000_000, row.BalanceSats);
        Assert.Equal(1, row.TxCount);
    }

    [Fact]
    public async Task Una_spesa_rimuove_l_utxo_e_produce_il_delta_negativo()
    {
        var account = Account();
        var scenario = new Scenario();
        var funding = scenario.Pay(account.GetReceiveAddress(0), 1_000_000, height: 100);

        // Spend: 600k to an external address, 390k change to change/0, 10k fee.
        var external = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);
        var spend = Net.CreateTransaction();
        spend.Inputs.Add(new TxIn(new OutPoint(funding, 0)));
        spend.Outputs.Add(Money.Satoshis(600_000), external);
        spend.Outputs.Add(Money.Satoshis(390_000), account.GetChangeAddress(0));
        scenario.Register(spend, 101, account.GetReceiveAddress(0), account.GetChangeAddress(0));

        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();

        // The funding UTXO is spent: only the change remains.
        var utxo = Assert.Single(result.Utxos);
        Assert.Equal(spend.GetHash().ToString(), utxo.Txid);
        Assert.True(utxo.IsChange);
        Assert.Equal(390_000, result.ConfirmedSats);
        Assert.Equal(1, result.NextChangeIndex);

        // History newest-first with the net deltas.
        Assert.Equal(2, result.History.Count);
        Assert.Equal(spend.GetHash().ToString(), result.History[0].Txid);
        Assert.Equal(-610_000, result.History[0].DeltaSats); // 390k change − 1M spent
        Assert.Equal(1_000_000, result.History[1].DeltaSats);
    }

    [Fact]
    public async Task Una_tx_in_mempool_conta_nel_saldo_non_confermato_e_non_verifica_merkle()
    {
        var account = Account();
        var scenario = new Scenario();
        scenario.Pay(account.GetReceiveAddress(0), 250_000, height: 0);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();

        Assert.Equal(0, result.ConfirmedSats);
        Assert.Equal(250_000, result.UnconfirmedSats);
        var entry = Assert.Single(result.History);
        Assert.False(entry.Verified);
        Assert.Equal(0, server.CallCount("blockchain.transaction.get_merkle"));
        Assert.Equal(0, server.CallCount("blockchain.block.header"));
    }

    [Fact]
    public async Task Un_coinbase_immaturo_finisce_nel_saldo_immaturo()
    {
        var account = Account();
        var scenario = new Scenario { TipHeight = 200 };
        // 11 confirmations at tip 200: far below CoinbaseMaturity+1 = 121.
        scenario.Pay(account.GetReceiveAddress(0), 5_000_000_000, height: 190, coinbase: true);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();

        Assert.Equal(5_000_000_000, result.ConfirmedSats);
        Assert.Equal(5_000_000_000, result.ImmatureSats);
        Assert.True(Assert.Single(result.Utxos).IsCoinbase);
    }

    // ---- sicurezza SPV ----

    [Fact]
    public async Task Una_prova_merkle_che_non_torna_con_l_header_fa_fallire_la_sync()
    {
        var account = Account();
        var scenario = new Scenario();
        var funding = scenario.Pay(account.GetReceiveAddress(0), 1_000_000, height: 100);
        // The server lies: the block 100 header commits to a different Merkle root.
        scenario.Headers[100] = Scenario.SingleTxHeaderHex(
            funding.GetHash(), 100, merkleRoot: uint256.One);

        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var ex = await Assert.ThrowsAsync<SpvVerificationException>(
            () => new WalletSynchronizer(account, client).SyncOnceAsync());
        Assert.Contains(funding.GetHash().ToString(), ex.Message);
    }

    // ---- resilienza ----

    [Fact]
    public async Task Gli_errori_server_busy_vengono_ritentati_fino_al_successo()
    {
        var account = Account();
        var scenario = new Scenario();
        scenario.Pay(account.GetReceiveAddress(0), 1_000_000, height: 100);

        var server = new FakeElectrumServer();
        scenario.WireTo(server);
        // The first 3 history calls fail with the ElectrumX throttling error.
        var failures = 3;
        var histories = scenario.History;
        server.Handle("blockchain.scripthash.get_history", p =>
        {
            if (Interlocked.Decrement(ref failures) >= 0)
                throw new FakeElectrumError(-102, "excessive resource usage");
            return (histories.GetValueOrDefault(p[0].GetString()!) ?? [])
                .Select(h => new { tx_hash = h.Txid, height = h.Height });
        });

        await using var _ = server;
        await using var client = await ElectrumClient.ConnectAsync(server.Host, server.Port, useSsl: false);

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();
        Assert.Equal(1_000_000, result.ConfirmedSats);
    }

    // ---- cache su disco ----

    [Fact]
    public async Task La_cache_precaricata_evita_di_riscaricare_tx_prove_e_header()
    {
        var account = Account();
        var scenario = new Scenario();
        scenario.Pay(account.GetReceiveAddress(0), 1_000_000, height: 100);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var first = new WalletSynchronizer(account, client);
        var result1 = await first.SyncOnceAsync();
        var (rawTx, verifiedAt, headers) = first.ExportCaches(Net);

        Assert.Single(rawTx);      // the confirmed tx is exported
        Assert.Single(verifiedAt); // with its verified height
        Assert.NotEmpty(headers);

        // Fresh synchroniser (new launch), warm cache: no re-download.
        server.ResetCallCounts();
        var second = new WalletSynchronizer(account, client);
        second.PreloadCaches(rawTx, verifiedAt, headers,
            result1.NextReceiveIndex, result1.NextChangeIndex, Net);
        var result2 = await second.SyncOnceAsync();

        Assert.Equal(result1.ConfirmedSats, result2.ConfirmedSats);
        Assert.Equal(0, server.CallCount("blockchain.transaction.get"));
        Assert.Equal(0, server.CallCount("blockchain.transaction.get_merkle"));
        Assert.Equal(0, server.CallCount("blockchain.block.header"));
    }

    [Fact]
    public async Task Le_tx_non_confermate_non_vengono_esportate_nella_cache()
    {
        var account = Account();
        var scenario = new Scenario();
        scenario.Pay(account.GetReceiveAddress(0), 250_000, height: 0); // mempool
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var sync = new WalletSynchronizer(account, client);
        await sync.SyncOnceAsync();
        var (rawTx, verifiedAt, _) = sync.ExportCaches(Net);

        // Unconfirmed txs can change (RBF): they must always be re-downloaded.
        Assert.Empty(rawTx);
        Assert.Empty(verifiedAt);
    }

    // ---- account a indirizzi fissi (WIF importati) ----

    [Fact]
    public async Task Un_account_con_indirizzi_fissi_scansiona_solo_quelli()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);
        var account = new ImportedKeyAccount([(address, key)], ScriptKind.NativeSegwit, Profile);

        var scenario = new Scenario();
        scenario.Pay(address, 750_000, height: 150);
        var (server, client) = await StartAsync(scenario);
        await using var _ = server; await using var __ = client;

        var result = await new WalletSynchronizer(account, client).SyncOnceAsync();

        Assert.Equal(750_000, result.ConfirmedSats);
        Assert.Single(result.Addresses);
        Assert.Equal(1, server.CallCount("blockchain.scripthash.get_history"));
        Assert.Equal(1, result.NextReceiveIndex);
    }
}
