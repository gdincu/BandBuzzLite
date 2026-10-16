namespace BandBuzzLite.Band;

/// <summary>Unified session for either Xiaomi FE95 or Huami/Zepp OS.</summary>
internal interface IBandSession : IDisposable
{
    bool IsReady { get; }
    int MaxWrite { get; set; }
    Task ConnectAsync(CancellationToken ct = default);
    void Disconnect();
    Task SendNotifAsync(string title, string body, string package, string appName, int nid, CancellationToken ct = default);
}
