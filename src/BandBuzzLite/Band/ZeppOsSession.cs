namespace BandBuzzLite.Band;

/// <summary>
/// Abstract Huami/Zepp OS session (FEE1 + chunked-2021), counterpart to XiaomiSession (FE95).
/// Gadgetbridge: service/devices/huami/zeppos/ZeppOsSupport.java:1127 (writeToChunked2021),
/// Huami2021ChunkedEncoder/Decoder.java, ZeppOsNotificationService.java:0x001e,
/// InitOperation2021.java:92-176 (ECDH B-163 + AES ECB).
/// Transport-agnostic: concrete WinRt/Win32 subclasses provide ATT write + notify dispatch.
/// </summary>
internal abstract class ZeppOsSession : IBandSession
{
    protected readonly string _mac;
    protected readonly byte[] _secret; // 16B authKey
    protected readonly Action<string> _log;

    protected byte[]? _sessionKey; // finalSharedSession 16B after ECDH
    protected int _seq;
    protected readonly HuamiFrame.Encoder _encoder = new() { Mtu = 247 };
    protected readonly HuamiFrame.Decoder _decoder = new(extended: true);
    protected readonly SemaphoreSlim _writeLock = new(1, 1);

    protected TaskCompletionSource<int>? _authTcs; // 0 success
    protected TaskCompletionSource<(int version, bool pics, bool key)>? _capsTcs;
    protected TaskCompletionSource<bool>? _notifAckTcs;

    public bool IsReady { get; protected set; }
    public int MaxWrite { get; set; } = 247;
    public int Mtu { get; set; } = 247;

    protected ZeppOsSession(string mac, string authKeyHex, Action<string>? log = null)
    {
        _mac = mac;
        _secret = XiaomiProtocol.ParseAuthKey(authKeyHex);
        _log = log ?? Console.WriteLine;
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    protected abstract Task TransportWriteAsync(byte[] chunk, CancellationToken ct);
    protected abstract void SendChunkedAck(byte handle, byte count);
    protected abstract void DisposeTransport();

    public void Disconnect() { DisposeTransport(); IsReady = false; _sessionKey = null; }
    public void Dispose() { DisposeTransport(); _writeLock.Dispose(); GC.SuppressFinalize(this); }

    public async Task SendNotifAsync(string title, string body, string package, string appName, int nid, CancellationToken ct = default)
    {
        if (!IsReady || _sessionKey is null) throw new InvalidOperationException("link not READY");
        var plain = ZeppOsProto.BuildNotification(title, body, package, appName, nid);
        await SendChunkedAsync(HuamiProtocol.EndpointNotification, plain, $"send '{title}'", true, ct);
        _log($"Sent (Zepp OS 0x001e): {title}");
    }

    protected async Task RunAuthAsync(CancellationToken ct)
    {
        // Step1: 04 02 00 02 + 48B pubEC via endpoint 0x0082, no encryption, extended
        var (prv, pub) = HuamiCrypto.GenerateKeyPair();
        SetPrivateEC(prv);
        var step1 = new byte[4 + 48];
        step1[0] = 0x04; step1[1] = 0x02; step1[2] = 0x00; step1[3] = 0x02;
        Buffer.BlockCopy(pub, 0, step1, 4, 48);
        _authTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _log("Zepp OS auth step1: sending ECDH pub...");
        await SendChunkedAsync(HuamiProtocol.EndpointAuth, step1, "huami auth step1", false, ct);
        var res = await AwaitOrThrowAsync(_authTcs.Task, TimeSpan.FromSeconds(15), "huami auth no reply", ct);
        if (res != 0) throw new InvalidOperationException($"Huami auth failed result={res}");
        IsReady = true; // decoder set up per earlier 10 04 01 handling
        _log("Zepp OS authenticated, link READY.");

        // Capabilities probe (optional but required for some bands to accept notifs)
        try
        {
            _capsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await SendChunkedAsync(HuamiProtocol.EndpointNotification, ZeppOsProto.BuildCapabilitiesRequest(), "caps probe", true, ct);
            await AwaitOrThrowAsync(_capsTcs.Task, TimeSpan.FromSeconds(5), "caps timeout", ct);
            _log("Caps probed");
        }
        catch (Exception ex) { _log($"caps probe failed (non-fatal): {ex.Message}"); }
    }

    protected async Task SendChunkedAsync(short endpoint, byte[] plain, string task, bool encrypt, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var chunks = _encoder.Encode(endpoint, plain, encrypt, extended: true);
            foreach (var c in chunks) await TransportWriteAsync(c, ct);
            // For encrypted notifs, band ack is via chunked ack (0x04 00 handle 01 count) – we fire-and-forget for now
            // ZeppOsNotificationService does not wait for an explicit ACK beyond chunked layer for SEND.
            await Task.Delay(120, ct); // pacing like Gadgetbridge TransactionBuilder queue
            _log($"{task}: {chunks.Count} chunk(s) sent");
        }
        finally { _writeLock.Release(); }
    }

    protected static async Task<T> AwaitOrThrowAsync<T>(Task<T> t, TimeSpan timeout, string msg, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { return await t.WaitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(msg); }
    }

    // Called from transport notify thread (00000017)
    protected void FeedReceived(byte[] data)
    {
        var decoded = _decoder.Decode(data);
        if (decoded is null)
        {
            // Maybe it's a chunked ack 04 00 handle 01 count
            if (data.Length == 5 && data[0] == 0x04 && data[1] == 0x00) return;
            return;
        }
        if (decoded.Value.needsAck) SendChunkedAck(_decoder.LastHandle, _decoder.LastCount);
        var (type, payload) = (decoded.Value.type, decoded.Value.payload);
        if (type == HuamiProtocol.EndpointAuth)
        {
            HandleAuthPayload(payload);
        }
        else if (type == HuamiProtocol.EndpointNotification)
        {
            HandleNotifPayload(payload);
        }
    }

    private void HandleAuthPayload(byte[] p)
    {
        // Expected forms: 10 04 01 <16B random><48B pub>  or 10 05 01 / 10 05 25
        if (p.Length >= 3 && p[0] == 0x10 && p[1] == 0x04 && p[2] == 0x01 && p.Length >= 3 + 16 + 48)
        {
            var remoteRandom = p[3..19];
            var remotePub = p[19..67];
            if (_lastPrivateEC is null) { _authTcs?.TrySetResult(-1); return; }
            var shared = EcdhB163.GenerateShared(_lastPrivateEC, remotePub);
            if (shared is null) { _authTcs?.TrySetResult(-1); return; }
            var (seq, sess) = HuamiCrypto.DeriveSession(shared, _secret);
            _seq = seq; _sessionKey = sess;
            _encoder.SetEncryptionParameters(seq, sess);
            _decoder.SetEncryptionParameters(sess);
            // Build step2 reply 05 + AES(...) + AES(...)
            var step2 = HuamiCrypto.BuildAuthStep2(_secret, sess, remoteRandom);
            _ = SendChunkedAsync(HuamiProtocol.EndpointAuth, step2, "huami auth step2", false, CancellationToken.None);
            return;
        }
        if (p.Length >= 3 && p[0] == 0x10 && p[1] == 0x05)
        {
            _authTcs?.TrySetResult(p[2] == 0x01 ? 0 : p[2]);
        }
    }
    private byte[]? _lastPrivateEC; // set in ConnectAsync before step1

    protected void SetPrivateEC(byte[] prv) => _lastPrivateEC = prv;

    private void HandleNotifPayload(byte[] p)
    {
        if (p.Length == 0) return;
        switch (p[0])
        {
            case HuamiProtocol.NotifCmdCapabilitiesResponse:
                if (p.Length >= 2) { var ver = p[1] & 0xff; _capsTcs?.TrySetResult((ver, false, false)); }
                break;
            case HuamiProtocol.NotifCmdReply:
            case HuamiProtocol.NotifCmdDismiss:
            case HuamiProtocol.NotifCmdIconRequest:
                // Band requests handled via ZeppOsNotificationService – log only for Lite
                _log($"Zepp OS notif evt 0x{p[0]:X2}");
                break;
        }
    }
}
