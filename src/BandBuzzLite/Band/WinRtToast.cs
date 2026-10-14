// Toast listener over raw WinRT: UserNotificationListener access + change
// events + notification reads with HSTRING plumbing. Mirrors the full
// BandBuzz NotificationWatcher behavior (filters, dedup, snapshot).

using Windows.Win32;

namespace BandBuzzLite.Band;

public sealed record ToastItem(uint Id, string App, string Title, string Body);

internal enum ToastAccess
{
    Unspecified = 0,
    Allowed = 1,
    Denied = 2,
}

/// <summary>
/// Windows toast listener: Chrome and Teams both post through the WinRT toast
/// pipeline, so this is the lightest reliable source. Needs package identity.
/// </summary>
internal sealed class ToastWatcher : IDisposable
{
    private const int ToastKind = 1;

    private readonly IReadOnlyList<string> _filters;
    private readonly TimeSpan _dedupWindow;
    private readonly Action<string> _log;

    private readonly Dictionary<uint, ToastItem> _seen = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private (string App, string Title, string Body, DateTimeOffset At)? _lastSent;
    private bool _subscribed;

    private nint _listenerObj;
    private EventRegistrationToken _token;
    private NotifChangedHandler? _handler;
    private nint _ccw;

    public event Action<ToastItem>? Added;

    public ToastWatcher(IEnumerable<string> filters, TimeSpan dedupWindow, Action<string>? log = null)
    {
        _filters = filters.ToList();
        _dedupWindow = dedupWindow;
        _log = log ?? Console.WriteLine;
    }

    public static bool HasPackageIdentity()
    {
        try
        {
            uint len = 0;
            var rc = PInvoke.GetCurrentPackageFullName(ref len, Span<char>.Empty);
            const uint APPMODEL_ERROR_NO_PACKAGE = 15700;
            const uint ERROR_INSUFFICIENT_BUFFER = 122;
            var v = (uint)rc;
            if (v == APPMODEL_ERROR_NO_PACKAGE)
                return false;
            return v == 0 || v == ERROR_INSUFFICIENT_BUFFER;
        }
        catch
        {
            return false;
        }
    }

    private static nint CurrentListener()
    {
        var statics = WinRt.Wrap<IListenerStatics>(WinRt.GetFactory(
            "Windows.UI.Notifications.Management.UserNotificationListener",
            new Guid(WinRtIids.ListenerStatics)));
        return statics.GetCurrent();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!HasPackageIdentity())
            throw new InvalidOperationException(
                "No package identity: you are running unpackaged (dotnet run / bin exe). " +
                "Windows only grants toast access to the installed identity package, " +
                "so this always reports Denied here. " +
                "Run install.ps1 from the repo root, then run the installed app. " +
                "BLE-only checks work unpackaged: BandBuzzLite --test ... / --selftest / --diag.");
        var listenerPtr = CurrentListener();
        var listener = WinRt.Wrap<IToastListener>(listenerPtr);
        var op = listener.RequestAccessAsync();
        await WinRt.WaitOpAsync(op, WinRtIids.OpAccess, "RequestAccessAsync", TimeSpan.FromSeconds(10), ct);
        var access = (ToastAccess)WinRt.Wrap<IOpAccess>(op).GetResults();
        if (access != ToastAccess.Allowed)
            throw new InvalidOperationException(
                $"Notification access: {access} - open Settings > Privacy > Notifications " +
                $"(Windows 10) or Privacy & security > Notifications (Windows 11), " +
                $"allow apps to access notifications, then re-run the installed app (publish\\BandBuzzLite.exe).");

        _listenerObj = WinRt.Query(listenerPtr, new Guid(WinRtIids.Listener));
        var li = WinRt.Wrap<IToastListener>(_listenerObj);

        // Snapshot current toasts as seen: only NEW arrivals get forwarded.
        foreach (var t in await ReadCurrentAsync(ct))
            _seen[t.Id] = t;

        if (!_subscribed)
        {
            _handler = new NotifChangedHandler(() =>
            {
                // Fire-and-forget: never block the WinRT callback. The gate
                // serializes refreshes so _seen is only touched by one at a time.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _refreshGate.WaitAsync();
                        try { await RefreshAsync(CancellationToken.None); }
                        finally { _refreshGate.Release(); }
                    }
                    catch (Exception ex) { _log($"refresh failed: {ex.Message}"); }
                });
            });
            _ccw = WinRt.ComInterfaceFor(_handler, WinRtIids.NotifChangedHandler);
            _token = li.AddNotificationChanged(_ccw);
            _subscribed = true;
        }
        _log($"Listening for [{string.Join(", ", _filters)}] (dedup {_dedupWindow.TotalSeconds:0}s).");
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var current = await ReadCurrentAsync(ct);
        var ids = new HashSet<uint>();
        foreach (var t in current)
        {
            ids.Add(t.Id);
            if (_seen.ContainsKey(t.Id))
                continue;
            _seen[t.Id] = t;
            if (Matches(t) && !IsDup(t))
            {
                _lastSent = (t.App, t.Title, t.Body, DateTimeOffset.UtcNow);
                Added?.Invoke(t);
            }
        }
        foreach (var id in _seen.Keys.Where(k => !ids.Contains(k)).ToList())
            _seen.Remove(id);
    }

    /// <summary>Current toasts (unfiltered) - for --list tuning.</summary>
    public async Task<IReadOnlyList<ToastItem>> ListCurrentAsync(CancellationToken ct = default)
        => await ReadCurrentAsync(ct);

    private static async Task<IReadOnlyList<ToastItem>> ReadCurrentAsync(CancellationToken ct)
    {
        var listener = WinRt.Wrap<IToastListener>(CurrentListener());
        var op = listener.GetNotificationsAsync(ToastKind);
        await WinRt.WaitOpAsync(op, WinRtIids.OpNotifVec, "GetNotificationsAsync", TimeSpan.FromSeconds(10), ct);
        var vec = WinRt.QueryWrap<IVecUserNotification>(
            WinRt.Wrap<IOpNotifVec>(op).GetResults(), WinRtIids.VecUserNotification);
        var out_ = new List<ToastItem>();
        var n = vec.GetSize();
        for (var i = 0u; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            vec.GetAt(i, out var ptr);
            try
            {
                var un = WinRt.Wrap<IUserNotification>(ptr);
                var id = un.GetId();
                var app = ReadAppName(un);
                var (title, body) = ReadTexts(un);
                out_.Add(new ToastItem(id, app, title, body));
            }
            catch { /* malformed toast - skip */ }
        }
        return out_;
    }

    private static string ReadAppName(IUserNotification un)
    {
        try
        {
            var appInfo = un.GetAppInfo();
            if (appInfo == 0)
                return "(unknown)";
            var display = WinRt.Wrap<IAppInfo>(appInfo).GetDisplayInfo();
            if (display == 0)
                return "(unknown)";
            var name = WinRt.ReadHString(WinRt.Wrap<IAppDisplayInfo>(display).GetDisplayName());
            return string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        }
        catch
        {
            return "(unknown)";
        }
    }

    private static (string Title, string Body) ReadTexts(IUserNotification un)
    {
        var texts = new List<string>();
        try
        {
            var notif = un.GetNotification();
            if (notif == 0)
                return ("(no title)", string.Empty);
            var visual = WinRt.Wrap<IToastNotification>(notif).GetVisual();
            if (visual == 0)
                return ("(no title)", string.Empty);
            var binding = WinRt.WithTempString("ToastGeneric",
                h => WinRt.Wrap<IToastVisual>(visual).GetBinding(h));
            if (binding == 0)
                return ("(no title)", string.Empty);
            var elems = WinRt.QueryWrap<IVecAdaptiveText>(
                WinRt.Wrap<IToastBinding>(binding).GetTextElements(), WinRtIids.VecAdaptiveText);
            var count = elems.GetSize();
            for (var i = 0u; i < count; i++)
            {
                elems.GetAt(i, out var ptr);
                var text = WinRt.ReadHString(WinRt.Wrap<IAdaptiveText>(ptr).GetText());
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text.Trim());
            }
        }
        catch { /* malformed toast - fall through */ }
        return (
            texts.Count > 0 ? texts[0] : "(no title)",
            texts.Count > 1 ? string.Join("  ·  ", texts.Skip(1)) : string.Empty);
    }

    private bool Matches(ToastItem t)
    {
        if (_filters.Count == 0)
            return true;
        return _filters.Any(f =>
            t.App.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            t.Title.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDup(ToastItem t)
    {
        if (_lastSent is not { } last)
            return false;
        return last.App == t.App && last.Title == t.Title && last.Body == t.Body
            && (DateTimeOffset.UtcNow - last.At) < _dedupWindow;
    }

    /// <summary>Non-throwing access probe for --diag (status or failure reason).</summary>
    public static async Task<string> ProbeAccessAsync()
    {
        if (!HasPackageIdentity())
            return "Unavailable (no package identity - unpackaged run)";
        try
        {
            var listener = WinRt.Wrap<IToastListener>(CurrentListener());
            var op = listener.RequestAccessAsync();
            await WinRt.WaitOpAsync(op, WinRtIids.OpAccess, "RequestAccessAsync", TimeSpan.FromSeconds(10), CancellationToken.None);
            return ((ToastAccess)WinRt.Wrap<IOpAccess>(op).GetResults()).ToString();
        }
        catch (Exception ex)
        {
            return $"Error 0x{ex.HResult:X8}: {ex.Message}";
        }
    }

    public string DescribeFilter(ToastItem t) => Matches(t) ? "match" : "filtered-out";

    public void Dispose()
    {
        if (_subscribed && _listenerObj != 0 && _ccw != 0)
        {
            try { WinRt.Wrap<IToastListener>(_listenerObj).RemoveNotificationChanged(_token); } catch { }
            _subscribed = false;
        }
        if (_ccw != 0)
        {
            try { System.Runtime.InteropServices.Marshal.Release(_ccw); } catch { }
            _ccw = 0;
        }
        _handler = null;
        _listenerObj = 0;
        _refreshGate.Dispose();
    }
}
