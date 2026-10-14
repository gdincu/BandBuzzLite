// Minimal WinRT runtime: activation, QI, async-polling, IBuffer, IID asserts.
// No Microsoft.Windows.SDK.NET.dll: everything here is BCL + raw P/Invoke
// (combase, api-ms-win-core-winrt-string) + ComWrappers source-gen.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BandBuzzLite.Band;

internal sealed class RtWrappers : StrategyBasedComWrappers
{
    public static readonly RtWrappers Instance = new();
}

internal static class WinRt
{
    // ---- low-level imports ----

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(nint classId, out nint instance);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(uint initType);

    private const uint RO_INIT_MULTITHREADED = 1;
    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    static WinRt()
    {
        // CsWinRT does this under the hood; raw interop must do it explicitly.
        var hr = RoInitialize(RO_INIT_MULTITHREADED);
        if (hr < 0 && hr != RPC_E_CHANGED_MODE)
            ThrowIfFailed(hr, "RoInitialize");
    }

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern unsafe void WindowsCreateStringReference(
        char* sourceString, uint length, byte* hstringHeader, nint* hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern nint WindowsGetStringRawBuffer(nint str, out uint length);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint str);

    /// <summary>Reads an [out,retval] HSTRING and frees it.</summary>
    public static string ReadHString(nint hstr)
    {
        if (hstr == 0)
            return "";
        try
        {
            var buf = WindowsGetStringRawBuffer(hstr, out var len);
            if (buf == 0 || len == 0)
                return "";
            return Marshal.PtrToStringUni(buf, (int)len) ?? "";
        }
        finally
        {
            WindowsDeleteString(hstr);
        }
    }

    /// <summary>Runs f with a stack-local HSTRING reference for s.</summary>
    public static unsafe T WithTempString<T>(string s, Func<nint, T> f)
    {
        byte* header = stackalloc byte[40];
        nint hstr;
        fixed (char* p = s)
            WindowsCreateStringReference(p, (uint)s.Length, header, &hstr);
        return f(hstr);
    }

    private static void ThrowIfFailed(int hr, string op)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{op} failed: 0x{hr:X8} ({Marshal.GetPInvokeErrorMessage(hr)})");
    }

    // ---- HSTRING (stack-local fast reference, no delete needed) ----

    private static unsafe nint RefString(string s, byte* header40, nint* hstr)
    {
        fixed (char* p = s)
            WindowsCreateStringReference(p, (uint)s.Length, header40, hstr);
        return *hstr;
    }

    public static unsafe nint Activate(string className)
    {
        byte* header = stackalloc byte[40];
        nint hstr;
        var hr = RoActivateInstance(RefString(className, header, &hstr), out var obj);
        ThrowIfFailed(hr, $"RoActivateInstance({className})");
        return obj;
    }

    public static unsafe nint GetFactory(string className, Guid iid)
    {
        byte* header = stackalloc byte[40];
        nint hstr;
        var hr = RoGetActivationFactory(RefString(className, header, &hstr), iid, out var factory);
        ThrowIfFailed(hr, $"RoGetActivationFactory({className})");
        return factory;
    }

    // ---- QI / wrapping ----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int QueryInterfaceFn(nint self, Guid* iid, nint* obj);

    public static unsafe nint Query(nint obj, Guid iid)
    {
        var vtable = Marshal.ReadIntPtr(obj);
        var qiPtr = Marshal.ReadIntPtr(vtable);
        var qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(qiPtr);
        nint outObj;
        var hr = qi(obj, &iid, &outObj);
        if (hr < 0 || outObj == 0)
            throw new InvalidOperationException($"QI for {iid} failed: 0x{hr:X8}");
        return outObj;
    }

    public static T Wrap<T>(nint ptr) where T : class =>
        (T)RtWrappers.Instance.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);

    public static T QueryWrap<T>(nint obj, string iid) where T : class =>
        Wrap<T>(Query(obj, new Guid(iid)));

    /// <summary>Self-validating QI: proves a (computed) IID against the OS.</summary>
    public static void AssertIid(nint obj, string iid, string what)
    {
        var insp = QueryWrap<IWinRtInspectable>(obj, WinRtIids.IInspectable);
        insp.GetIids(out var count, out var array);
        try
        {
            var want = new Guid(iid);
            var found = new List<string>();
            for (var i = 0; i < (int)count; i++)
            {
                var g = Marshal.PtrToStructure<Guid>(array + i * 16);
                if (g == want)
                    return;
                found.Add(g.ToString("D").ToUpperInvariant());
            }
            throw new InvalidOperationException(
                $"IID assert failed for {what}: {iid} not in object identity set " +
                $"[{string.Join(" ", found)}] - interop metadata is wrong, aborting.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(array);
        }
    }

    // ---- async (poll IAsyncInfo; no Completed-handler CCW needed) ----

    private const int AsyncStarted = 0;
    private const int AsyncCompleted = 1;
    private const int AsyncError = 3;

    public static async Task<nint> WaitOpAsync(
        nint op, string opIid, string opName, TimeSpan timeout, CancellationToken ct)
    {
        AssertIid(op, opIid, opName);
        var info = QueryWrap<IWinRtAsyncInfo>(op, WinRtIids.IAsyncInfo);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var st = info.GetStatus();
            if (st == AsyncCompleted)
                return op;
            if (st == AsyncError)
                throw new InvalidOperationException($"{opName} failed (async error 0x{info.GetErrorCode():X8})");
            if (st != AsyncStarted)
                throw new InvalidOperationException($"{opName} ended unexpectedly (status={st})");
            if (sw.Elapsed > timeout)
                throw new TimeoutException($"{opName} timed out after {timeout.TotalSeconds:0}s");
            await Task.Delay(25, ct);
        }
    }

    // ---- IBuffer (factory-made; bytes via IBufferByteAccess) ----

    private static nint? _bufferFactory;

    public static nint CreateBuffer(byte[] data)
    {
        _bufferFactory ??= GetFactory(
            "Windows.Storage.Streams.Buffer", new Guid(WinRtIids.BufferFactory));
        var factory = Wrap<IBufferFactory>(_bufferFactory.Value);
        var raw = factory.Create((uint)data.Length);
        var buf = Wrap<IWinRtBuffer>(raw);
        buf.SetLength((uint)data.Length);
        var access = QueryWrap<IBufferByteAccess>(raw, WinRtIids.BufferByteAccess);
        access.GetBuffer(out var p);
        Marshal.Copy(data, 0, p, data.Length);
        return raw;
    }

    public static byte[] ReadBuffer(nint buffer)
    {
        var buf = Wrap<IWinRtBuffer>(buffer);
        var len = (int)buf.GetLength();
        if (len <= 0 || len > 4096)
            return [];
        var access = QueryWrap<IBufferByteAccess>(buffer, WinRtIids.BufferByteAccess);
        access.GetBuffer(out var p);
        var out_ = new byte[len];
        Marshal.Copy(p, out_, 0, len);
        return out_;
    }

    private static nint WrapToPtr(object rcw) =>
        RtWrappers.Instance.GetOrCreateComInterfaceForObject(rcw, CreateComInterfaceFlags.None);

    public static nint ComInterfaceFor(object managed, string iid)
    {
        var ptr = WrapToPtr(managed);
        // Fail fast if the CCW doesn't advertise the delegate IID.
        var probed = Query(ptr, new Guid(iid));
        Marshal.Release(probed);
        return ptr;
    }
}
