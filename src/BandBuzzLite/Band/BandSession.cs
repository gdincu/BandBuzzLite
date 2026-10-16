namespace BandBuzzLite.Band;

/// <summary>Shared base for Xiaomi FE95 and Huami Zepp OS sessions. De-duplicates AwaitOrThrowAsync / _writeLock / IsReady.</summary>
internal abstract class BandSession : IBandSession
{
    protected readonly string _mac;
    protected readonly byte[] _secret;
    protected readonly Action<string> _log;
    protected readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsReady { get; protected set; }
    public int MaxWrite { get; set; }
    public int Mtu { get; set; }

    protected BandSession(string mac, string authKeyHex, int defaultMaxWrite, int defaultMtu, Action<string>? log = null)
    {
        _mac = mac;
        _secret = XiaomiProtocol.ParseAuthKey(authKeyHex);
        _log = log ?? Console.WriteLine;
        MaxWrite = defaultMaxWrite;
        Mtu = defaultMtu;
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task SendNotifAsync(string title, string body, string package, string appName, int nid, CancellationToken ct = default);
    public abstract void Disconnect();
    public abstract void Dispose();

    protected static async Task<T> AwaitOrThrowAsync<T>(Task<T> t, TimeSpan timeout, string msg, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { return await t.WaitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(msg); }
    }
}
