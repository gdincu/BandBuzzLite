// Transport-agnostic Xiaomi link session: connect -> auth -> READY ->
// encrypted sends. Concrete transports (Win32 GATT, raw WinRT) provide the
// single-packet write, the ack write, and teardown; everything else - the
// handshake, chunking, ack matching - is shared here and identical for both.

namespace BandBuzzLite.Band;

/// <summary>
/// Xiaomi BLE link for FE95 encrypted bands (Band 8 Active, Band 7 Pro, Band 8/9/...).
/// Owns ONE connection. Plain Mi Band 7 (Huami/Zepp OS) is NOT an FE95 device – see XiaomiProtocol.
/// </summary>
internal abstract class XiaomiSession : IDisposable
{
    protected readonly string _mac;
    protected readonly byte[] _secret;
    protected readonly Action<string> _log;

    protected XiaomiCrypto.SessionKeys? _keys;
    protected int _encIndex = 1;

    protected readonly XiaomiFrame.Reassembler _reassembler = new();
    protected readonly SemaphoreSlim _writeLock = new(1, 1);

    // Signalled from the notify thread; consumed by the auth/send flow.
    protected TaskCompletionSource<int>? _ackTcs;
    protected TaskCompletionSource<bool>? _chunkStartTcs;
    protected TaskCompletionSource<bool>? _chunkEndTcs;
    protected TaskCompletionSource<(bool Encrypted, byte[] Payload)>? _cmdTcs;
    protected int[]? _pendingResend;

    public bool IsReady { get; protected set; }

    // 247-3 ATT overhead, same as the tested pc_test value.
    public int MaxWrite { get; set; } = 244;

    protected XiaomiSession(string mac, string authKeyHex, Action<string>? log = null)
    {
        _mac = mac;
        _secret = XiaomiProtocol.ParseAuthKey(authKeyHex);
        _log = log ?? Console.WriteLine;
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);

    /// <summary>One ATT write; throws InvalidOperationException("ATT write failed...") on transport failure.</summary>
    protected abstract Task TransportWriteAsync(byte[] packet, CancellationToken ct);

    /// <summary>Best-effort ack write from the notify thread (never throws).</summary>
    protected abstract void SendAck(byte[] packet);

    protected abstract void DisposeTransport();

    public void Disconnect()
    {
        DisposeTransport();
        IsReady = false;
    }

    public void Dispose()
    {
        DisposeTransport();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task SendNotifAsync(string title, string body, string package, string appName, int nid,
        CancellationToken ct = default)
    {
        if (!IsReady || _keys is null)
            throw new InvalidOperationException("link not READY");
        var plain = XiaomiProto.BuildNotif(title, body, package, appName, nid);
        await SendCommandAsync(plain, $"send '{title}'", ct);
        _log($"Sent: {title}");
    }

    protected async Task RunAuthAsync(CancellationToken ct)
    {
        var phoneNonce = XiaomiCrypto.RandomNonce();
        _log("Auth step 1: sending phone nonce...");
        // Arm the reply waiter BEFORE writing: a fast band reply arriving
        // between the ack and a later-created waiter would otherwise be dropped.
        _cmdTcs = new TaskCompletionSource<(bool, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
        await RawWriteAsync(
            XiaomiFrame.EncodeSingle(XiaomiProto.BuildStep1(phoneNonce), false, 0),
            "auth step 1", ct);

        var (enc, payload) = await AwaitOrThrowAsync(_cmdTcs.Task, TimeSpan.FromSeconds(15), "timed out waiting for band reply", ct);
        var (watchNonce, watchHmac) = XiaomiProto.ParseWatchNonce(payload);
        _log($"Auth step 2: watch nonce {watchNonce.Length}B received, verifying...");

        var handled = XiaomiCrypto.HandleWatchNonce(_secret, phoneNonce, watchNonce, watchHmac);
        if (handled is null)
            throw new InvalidOperationException("Auth FAILED: HMAC mismatch - authKey is wrong for this band.");
        var keys = handled.Value.Keys;
        _keys = keys;

        _cmdTcs = new TaskCompletionSource<(bool, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
        await RawWriteAsync(
            XiaomiFrame.EncodeSingle(handled.Value.Step3, false, 0),
            "auth step 3", ct);

        var (enc2, payload2) = await AwaitOrThrowAsync(_cmdTcs.Task, TimeSpan.FromSeconds(15), "timed out waiting for band reply", ct);
        if (enc2)
            payload2 = XiaomiCrypto.Decrypt(keys.DecryptionKey, keys.DecryptionNonce, 0, payload2);
        var (type, sub) = XiaomiProto.ParseCommandType(payload2);
        if (type != XiaomiProtocol.AuthType || sub != XiaomiProtocol.CmdAuth)
            _log($"Unexpected auth confirm (type={type} sub={sub}), continuing anyway.");

        IsReady = true;
        _log("Authenticated, link READY.");
        try
        {
            await SendCommandAsync(XiaomiProto.BuildScreenOn(), "screen-on-on-notifications", ct);
        }
        catch (Exception ex)
        {
            _log($"screen-on-on-notifications failed: {ex.Message}");
        }
    }

    // ---- command write: RawWriteAsync serializes individual writes internally ----

    protected async Task SendCommandAsync(byte[] plain, string task, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await SendCommandOnceAsync(plain, task, ct);
                return;
            }
            catch (InvalidOperationException ex)
                when (attempt < 4 && MaxWrite > 20 &&
                      ex.Message.StartsWith("ATT write failed", StringComparison.Ordinal))
            {
                attempt++;
                MaxWrite = Math.Max(20, MaxWrite / 2);
                _log($"{task}: write rejected, shrinking MaxWrite to {MaxWrite} and retrying ({attempt}/4)");
            }
        }
    }

    private async Task SendCommandOnceAsync(byte[] plain, string task, CancellationToken ct)
    {
        var ready = IsReady && _keys is not null;
        var bytes = ready
            ? XiaomiCrypto.Encrypt(_keys!.EncryptionKey, _keys.EncryptionNonce, _encIndex, plain)
            : plain;

        if (!XiaomiFrame.ShouldWriteChunked(bytes.Length, ready, MaxWrite))
        {
            var packet = XiaomiFrame.EncodeSingle(bytes, ready, ready ? _encIndex : 0);
            if (ready)
                _encIndex++;
            await RawWriteAsync(packet, task, ct);
            return;
        }

        var payload = ready ? XiaomiFrame.PrependEncIndex(bytes, _encIndex) : bytes;
        if (ready)
            _encIndex++;
        var count = XiaomiFrame.ChunkCount(payload.Length, MaxWrite);

        _chunkStartTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResend = null;
        await TransportWriteAsync(XiaomiFrame.EncodeChunkedStart(count, ready), ct);
        await AwaitOrThrowAsync(_chunkStartTcs.Task, TimeSpan.FromSeconds(5), $"{task}: no chunk-start ack", ct);
        _log($"{task}: chunked-start acked ({count} chunks)");

        var indices = _pendingResend ?? Enumerable.Range(1, count).ToArray();
        _chunkEndTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stride = MaxWrite - 2;
        foreach (var idx in indices)
        {
            var from = (idx - 1) * stride;
            var to = Math.Min(idx * stride, payload.Length);
            await TransportWriteAsync(XiaomiFrame.EncodeChunk(idx, payload[from..to]), ct);
        }
        await AwaitOrThrowAsync(_chunkEndTcs.Task, TimeSpan.FromSeconds(8), $"{task}: no chunk-end ack", ct);
        _log($"{task}: chunks acked");
    }

    /// <summary>
    /// Write + wait for band ack(0). The lock is not reentrant: it is only
    /// ever held inside this method, never across calls.
    /// </summary>
    private async Task RawWriteAsync(byte[] packet, string task, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _ackTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            await TransportWriteAsync(packet, ct);
            var res = await AwaitOrThrowAsync(_ackTcs.Task, TimeSpan.FromSeconds(5), $"{task}: no ack", ct);
            if (res != 0)
                throw new InvalidOperationException($"{task}: band nack result={res}");
            _log($"{task}: ack ok");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    protected static async Task<T> AwaitOrThrowAsync<T>(Task<T> t, TimeSpan timeout, string msg, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await t.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(msg);
        }
    }

    // ---- inbound (notify thread: decode fast, never block) ----

    /// <summary>Feeds one notification payload from the transport into the session.</summary>
    protected void FeedReceived(byte[] data)
    {
        var msg = XiaomiFrame.Decode(data);
        SignalAcks(msg);
        switch (msg)
        {
            case XiaomiFrame.Incoming.Single s:
                // Protocol requires acking every incoming single, else the band stalls.
                SendAck(XiaomiFrame.PayloadAck);
                byte[] payload = s.Payload;
                if (s.Encrypted && _keys is not null)
                {
                    try { payload = XiaomiCrypto.Decrypt(_keys.DecryptionKey, _keys.DecryptionNonce, 0, payload); }
                    catch { /* fall through with raw */ }
                }
                _cmdTcs?.TrySetResult((s.Encrypted, payload));
                break;
            case XiaomiFrame.Incoming.ChunkedStart cs:
                SendAck(XiaomiFrame.ChunkStartAck);
                _reassembler.FeedStart(cs);
                break;
            case XiaomiFrame.Incoming.Chunk c:
                var full = _reassembler.Feed(c);
                if (full is not null)
                {
                    SendAck(XiaomiFrame.ChunkEndAck);
                    _cmdTcs?.TrySetResult((true, full));
                }
                break;
        }
    }

    private void SignalAcks(XiaomiFrame.Incoming msg)
    {
        switch (msg)
        {
            case XiaomiFrame.Incoming.Ack a:
                _ackTcs?.TrySetResult(a.Result);
                break;
            case XiaomiFrame.Incoming.ChunkAckStart:
                _chunkStartTcs?.TrySetResult(true);
                break;
            case XiaomiFrame.Incoming.ChunkAckEnd:
                _chunkEndTcs?.TrySetResult(true);
                break;
            case XiaomiFrame.Incoming.ChunkNack:
                _chunkEndTcs?.TrySetResult(true);
                _ackTcs?.TrySetResult(-1);
                break;
            case XiaomiFrame.Incoming.ChunkResend r:
                _pendingResend = r.Indices;
                _chunkStartTcs?.TrySetResult(true);
                break;
        }
    }
}
