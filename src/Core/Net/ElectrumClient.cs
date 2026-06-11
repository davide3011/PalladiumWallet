using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PalladiumWallet.Core.Net;

/// <summary>
/// Client del protocollo del server di indicizzazione (blueprint §10):
/// JSON-RPC 2.0 newline-delimited su TCP, opzionalmente TLS con pinning TOFU.
/// Le notifiche (subscription) arrivano sull'evento <see cref="NotificationReceived"/>.
/// </summary>
public sealed class ElectrumClient : IAsyncDisposable
{
    public const string ClientName = "PalladiumWallet";
    public const string ProtocolVersion = "1.4";

    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private long _nextId;

    public string Host { get; }
    public int Port { get; }
    public bool UseSsl { get; }
    public bool IsConnected => _tcp.Connected && !_cts.IsCancellationRequested;

    /// <summary>(metodo, parametri) per le notifiche di subscription.</summary>
    public event Action<string, JsonElement>? NotificationReceived;

    /// <summary>Scatta quando la connessione cade (errore di lettura o chiusura remota).</summary>
    public event Action<Exception?>? Disconnected;

    private ElectrumClient(TcpClient tcp, Stream stream, string host, int port, bool useSsl)
    {
        _tcp = tcp;
        _stream = stream;
        Host = host;
        Port = port;
        UseSsl = useSsl;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// Connette al server. Con <paramref name="useSsl"/> la validazione del
    /// certificato è TOFU tramite <paramref name="pins"/> (§9): primo contatto
    /// salva, contatti successivi confrontano; mismatch ⇒ <see cref="CertificatePinMismatchException"/>.
    /// </summary>
    public static async Task<ElectrumClient> ConnectAsync(string host, int port, bool useSsl,
        CertificatePinStore? pins = null, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, ct);
            Stream stream = tcp.GetStream();
            if (useSsl)
            {
                var pinOk = true;
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                    (_, cert, _, _) =>
                    {
                        // I server Electrum sono tipicamente self-signed: la
                        // fiducia è il pin TOFU, non la catena CA (§9).
                        if (cert is null)
                            return false;
                        pinOk = pins is null || pins.VerifyOrPin(host, port, cert);
                        return pinOk;
                    });
                try
                {
                    await ssl.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions { TargetHost = host }, ct);
                }
                catch (System.Security.Authentication.AuthenticationException) when (!pinOk)
                {
                    throw new CertificatePinMismatchException(host, port);
                }
                stream = ssl;
            }

            var client = new ElectrumClient(tcp, stream, host, port, useSsl);
            // Negoziazione obbligatoria prima di ogni altra richiesta.
            await client.RequestAsync("server.version", ct, ClientName, ProtocolVersion);
            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> RequestAsync(string method, CancellationToken ct = default,
        params object?[] parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        });

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(payload, ct);
            await _stream.WriteAsync("\n"u8.ToArray(), ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }

        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null)
                    break; // chiusura remota
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                {
                    if (!_pending.TryRemove(idEl.GetInt64(), out var tcs))
                        continue;
                    if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                        tcs.TrySetException(new ElectrumServerException(error.ToString()));
                    else
                        tcs.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("method", out var methodEl))
                {
                    NotificationReceived?.Invoke(
                        methodEl.GetString()!,
                        root.GetProperty("params").Clone());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            foreach (var (_, tcs) in _pending)
                tcs.TrySetException(failure ?? new IOException("Connessione al server chiusa."));
            _pending.Clear();
            Disconnected?.Invoke(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _tcp.Close();
        try { await _readLoop; } catch { /* in chiusura */ }
        _cts.Dispose();
        _writeLock.Dispose();
    }
}

/// <summary>Errore restituito dal server (campo "error" della risposta JSON-RPC).</summary>
public sealed class ElectrumServerException(string error) : Exception(error);

/// <summary>
/// Il certificato del server è cambiato rispetto a quello salvato (TOFU, §9).
/// Si sblocca con il reset esplicito dei certificati.
/// </summary>
public sealed class CertificatePinMismatchException(string host, int port) : Exception(
    $"Il certificato TLS di {host}:{port} è cambiato rispetto a quello salvato. " +
    "Se il server ha rinnovato il certificato, esegui il reset dei certificati SSL.");
