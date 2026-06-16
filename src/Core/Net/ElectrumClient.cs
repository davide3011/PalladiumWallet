using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

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
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private readonly Task _writeLoop;

    // Channel single-reader: le task scrivono i payload senza lock;
    // il write loop drena tutto in un unico WriteAsync+FlushAsync —
    // identico al buffered writer asyncio di Electrum.
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    // Tetto alle richieste in volo (non in coda di scrittura, ma in attesa di
    // risposta sul server). Il write-loop batcha già l'invio in un solo segmento;
    // questo gate evita di sommergere il server con migliaia di richieste
    // simultanee su wallet grandi → niente -101/-102 a raffica né drop della
    // connessione. Le scritture restano comunque pipelinate fino a questo grado.
    private const int MaxInFlight = 32;
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight, MaxInFlight);

    private long _nextId;

    public string Host { get; }
    public int Port { get; }
    public bool UseSsl { get; }
    public bool IsConnected => _tcp.Connected && !_cts.IsCancellationRequested;

    public event Action<string, JsonElement>? NotificationReceived;
    public event Action<Exception?>? Disconnected;

    private ElectrumClient(TcpClient tcp, Stream stream, string host, int port, bool useSsl)
    {
        _tcp = tcp;
        _stream = stream;
        Host = host;
        Port = port;
        UseSsl = useSsl;
        _readLoop  = Task.Run(ReadLoopAsync);
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    public static async Task<ElectrumClient> ConnectAsync(string host, int port, bool useSsl,
        CertificatePinStore? pins = null, CancellationToken ct = default)
    {
        var tcp = new TcpClient { NoDelay = true };
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
                        if (cert is null) return false;
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
        // Gate prima di mettere in volo: oltre MaxInFlight richieste in attesa
        // si attende che una risposta liberi uno slot, invece di sommergere il server.
        await _inFlight.WaitAsync(ct);
        try
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

            _outgoing.Writer.TryWrite(payload);

            await using var registration = ct.Register(() =>
            {
                _pending.TryRemove(id, out _);
                tcs.TrySetCanceled(ct);
            });
            return await tcs.Task;
        }
        finally
        {
            _inFlight.Release();
        }
    }

    /// <summary>
    /// Drain loop: svuota il channel in un unico buffer → un solo WriteAsync+FlushAsync
    /// per tutti i messaggi in coda. Quando N richieste sono in coda, vengono
    /// trasmesse in un singolo segmento TCP invece di N flush seriali.
    /// </summary>
    private async Task WriteLoopAsync()
    {
        try
        {
            while (await _outgoing.Reader.WaitToReadAsync(_cts.Token))
            {
                using var ms = new MemoryStream(512);
                while (_outgoing.Reader.TryRead(out var data))
                {
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
                await _stream.WriteAsync(ms.GetBuffer().AsMemory(0, (int)ms.Length), _cts.Token);
                await _stream.FlushAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            foreach (var (_, pending) in _pending)
                pending.TrySetException(ex);
        }
    }

    /// <summary>
    /// Read loop con PipeReader: buffer pooled, zero allocazioni per-risposta
    /// (nessuna stringa intermedia), parsing JSON direttamente da byte span.
    /// </summary>
    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        var pipe = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            while (true)
            {
                ReadResult result;
                try { result = await pipe.ReadAsync(_cts.Token); }
                catch (OperationCanceledException) { break; }

                var buffer = result.Buffer;
                try
                {
                    while (TrySliceLine(ref buffer, out var line))
                        if (!line.IsEmpty)
                            DispatchLine(line);
                }
                finally
                {
                    // consumed = tutto ciò che abbiamo consumato (fino all'ultimo \n)
                    // examined = tutto ciò che abbiamo guardato (fino alla fine del buffer)
                    pipe.AdvanceTo(buffer.Start, buffer.End);
                }

                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await pipe.CompleteAsync();
            foreach (var (_, tcs) in _pending)
                tcs.TrySetException(failure ?? new IOException("Connessione al server chiusa."));
            _pending.Clear();
            Disconnected?.Invoke(failure);
        }
    }

    private static bool TrySliceLine(ref ReadOnlySequence<byte> buffer,
        out ReadOnlySequence<byte> line)
    {
        var pos = buffer.PositionOf((byte)'\n');
        if (pos is null) { line = default; return false; }
        line   = buffer.Slice(0, pos.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, pos.Value));
        return true;
    }

    private void DispatchLine(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            DispatchSpan(line.FirstSpan);
            return;
        }
        // Multi-segmento (risposta molto lunga): copia su ArrayPool poi parsa.
        var len = (int)line.Length;
        var buf = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            line.CopyTo(buf);
            DispatchSpan(buf.AsSpan(0, len));
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    private void DispatchSpan(ReadOnlySpan<byte> utf8)
    {
        // JsonDocument.Parse via Utf8JsonReader: nessuna stringa intermedia,
        // parsing direttamente dallo span pooled.
        var reader = new Utf8JsonReader(utf8);
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
        {
            if (!_pending.TryRemove(idEl.GetInt64(), out var tcs)) return;
            if (root.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
                tcs.TrySetException(new ElectrumServerException(err.ToString()));
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

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _outgoing.Writer.Complete();
        _tcp.Close();
        try { await _readLoop;  } catch { }
        try { await _writeLoop; } catch { }
        _cts.Dispose();
        _inFlight.Dispose();
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
