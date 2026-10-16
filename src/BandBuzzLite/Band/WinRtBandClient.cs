// Xiaomi BLE link over hand-rolled raw WinRT (no WinRT projection DLL).
// Connects from raw LE advertisements like the full BandBuzz app, so it
// reaches bands Windows never listed (unpaired) - the Win32 transport's
// blind spot. Session logic (auth/chunks/acks) is shared via XiaomiSession.

namespace BandBuzzLite.Band;

internal sealed class WinRtBandClient : XiaomiSession
{
    private EventRegistrationToken _rxToken;
    private RecvHandler? _rxHandler;
    private nint _rxCcw;

    private nint _deviceObj;
    private nint _charReadV1;
    private nint _charWriteV1;
    private nint _charWriteV3;
    private EventRegistrationToken _vcReadToken;
    private EventRegistrationToken _vcWriteToken;
    private ValueHandler? _vcHandler;
    private nint _vcCcw;
    private bool _vcWriteSubscribed;

    public WinRtBandClient(string mac, string authKeyHex, Action<string>? log = null)
        : base(mac, authKeyHex, log)
    {
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposeTransport();
        IsReady = false;
        _keys = null;
        _encIndex = 1;

        var addr = XiaomiProtocol.ParseMac(_mac);
        await WaitForAdvertisementAsync(addr, TimeSpan.FromSeconds(20), ct);

        _log($"Connecting to {_mac} ... (band takes 1 connection - close phone app first)");
        var statics = WinRt.Wrap<IDeviceStatics>(WinRt.GetFactory(
            "Windows.Devices.Bluetooth.BluetoothLEDevice", new Guid(WinRtIids.DeviceStatics)));
        var op = statics.FromBluetoothAddressAsync(addr);
        await WinRt.WaitOpAsync(op, WinRtIids.OpDevice, "FromBluetoothAddressAsync", TimeSpan.FromSeconds(20), ct);
        _deviceObj = WinRt.Wrap<IOpDevice>(op).GetResults();
        if (_deviceObj == 0)
            throw new InvalidOperationException($"BLE device not found: {_mac}");

        var dev3 = WinRt.QueryWrap<IDevice3>(_deviceObj, WinRtIids.Device3);
        var svcOp = dev3.GetGattServicesAsync();
        await WinRt.WaitOpAsync(svcOp, WinRtIids.OpServices, "GetGattServicesAsync", TimeSpan.FromSeconds(15), ct);
        var svcRes = WinRt.Wrap<IServicesResult>(WinRt.Wrap<IOpServices>(svcOp).GetResults());
        if ((GattStatus)svcRes.GetStatus() != GattStatus.Success)
            throw new InvalidOperationException($"GATT service discovery failed: {(GattStatus)svcRes.GetStatus()}");

        var svcVec = WinRt.QueryWrap<IVecServices>(svcRes.GetServices(), WinRtIids.VecServices);
        nint? fe95 = null;
        var nSvc = svcVec.GetSize();
        for (var i = 0u; i < nSvc; i++)
        {
            svcVec.GetAt(i, out var svcPtr);
            if (WinRt.QueryWrap<IService1>(svcPtr, WinRtIids.Service1).GetUuid() == XiaomiProtocol.ServiceFe95)
            {
                fe95 = svcPtr;
                break;
            }
        }
        if (fe95 is null)
            throw new InvalidOperationException(
                "FE95 service not found – device is not an encrypted Xiaomi FE95 band. " +
                "Plain Xiaomi Smart Band 7 (no Pro, Zepp OS/Huami) does not expose FE95 and is unsupported. " +
                "Supported: Band 7 Pro, 8 Active, 8/8 Pro, 9/9 Pro, Redmi Smart Band 2/Pro etc. (Gadgetbridge Xiaomi family).");

        var svc3 = WinRt.QueryWrap<IService3>(fe95.Value, WinRtIids.Service3);
        var chOp = svc3.GetCharacteristicsAsync();
        await WinRt.WaitOpAsync(chOp, WinRtIids.OpCharacteristics, "GetCharacteristicsAsync", TimeSpan.FromSeconds(15), ct);
        var chRes = WinRt.Wrap<ICharacteristicsResult>(WinRt.Wrap<IOpCharacteristics>(chOp).GetResults());
        if ((GattStatus)chRes.GetStatus() != GattStatus.Success)
            throw new InvalidOperationException($"Characteristic discovery failed: {(GattStatus)chRes.GetStatus()}");

        var chVec = WinRt.QueryWrap<IVecCharacteristics>(chRes.GetCharacteristics(), WinRtIids.VecCharacteristics);
        var nCh = chVec.GetSize();
        for (var i = 0u; i < nCh; i++)
        {
            chVec.GetAt(i, out var chPtr);
            var uuid = WinRt.QueryWrap<ICharacteristic1>(chPtr, WinRtIids.Characteristic1).GetUuid();
            if (uuid == XiaomiProtocol.CharCommandRead)
                _charReadV1 = chPtr;
            else if (uuid == XiaomiProtocol.CharCommandWrite)
            {
                _charWriteV1 = chPtr;
                _charWriteV3 = WinRt.Query(chPtr, new Guid(WinRtIids.Characteristic3));
            }
        }
        if (_charReadV1 == 0)
            throw new InvalidOperationException("0x51 characteristic missing");
        if (_charWriteV1 == 0)
            throw new InvalidOperationException("0x52 characteristic missing");

        // Subscribe BEFORE any write, on both chars like the Android app + pc_test.
        _vcHandler = new ValueHandler(OnValueChanged);
        _vcCcw = WinRt.ComInterfaceFor(_vcHandler, WinRtIids.ValueHandler);
        Subscribe(_charReadV1, ref _vcReadToken, ct);
        try { Subscribe(_charWriteV1, ref _vcWriteToken, ct); _vcWriteSubscribed = true; }
        catch { /* some stacks refuse 0x52 notify */ }
        _log("Connected, notifications on.");

        await RunAuthAsync(ct);
    }

    private void Subscribe(nint charV1, ref EventRegistrationToken token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ch3 = WinRt.QueryWrap<ICharacteristic3>(charV1, WinRtIids.Characteristic3);
        var cccdOp = ch3.WriteCccdWithResultAsync((int)CccdValue.Notify);
        // CCCD is synchronous-friendly: short blocking wait is fine here.
        // NOTE: despite the winmd display, this returns IAsyncOperation<GattWriteResult>
        // (per cppwinrt + the live OS object identity) - not IAsyncOperation<GattCommunicationStatus>.
        WinRt.WaitOpAsync(cccdOp, WinRtIids.OpWrite, "WriteCCCD", TimeSpan.FromSeconds(10), ct)
            .GetAwaiter().GetResult();
        var st = WinRt.Wrap<IWriteResult>(WinRt.Wrap<IOpWrite>(cccdOp).GetResults()).GetStatus();
        if ((GattStatus)st != GattStatus.Success)
            throw new InvalidOperationException($"CCCD write failed: {(GattStatus)st}");
        var ch1 = WinRt.Wrap<ICharacteristic1>(charV1);
        token = ch1.AddValueChanged(_vcCcw);
    }

    private async Task WaitForAdvertisementAsync(ulong addr, TimeSpan timeout, CancellationToken ct)
    {
        _log("Scanning for the band...");
        var watcherObj = WinRt.Activate("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher");
        var watcher = WinRt.QueryWrap<IAdvertWatcher>(watcherObj, WinRtIids.Watcher);
        watcher.SetScanningMode(1); // Active
        var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _rxHandler = new RecvHandler(argsPtr =>
        {
            try
            {
                var args = WinRt.Wrap<IReceivedArgs>(argsPtr);
                if (args.GetBluetoothAddress() == addr)
                    seen.TrySetResult(true);
            }
            catch { /* never let native see managed exceptions */ }
        });
        _rxCcw = WinRt.ComInterfaceFor(_rxHandler, WinRtIids.RecvHandler);
        _rxToken = watcher.AddReceived(_rxCcw);
        try
        {
            watcher.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"BLE scan failed (radio off?): {ex.Message}", ex);
        }
        try
        {
            if (!await AwaitOrThrowAsync(seen.Task, timeout, $"no BLE advertisement from {_mac} (band nearby and advertising?)", ct))
                throw new InvalidOperationException("BLE scan stopped unexpectedly (radio off?)");
        }
        finally
        {
            try { watcher.Stop(); } catch { }
            try { watcher.RemoveReceived(_rxToken); } catch { }
            if (_rxCcw != 0)
            {
                System.Runtime.InteropServices.Marshal.Release(_rxCcw);
                _rxCcw = 0;
            }
            _rxHandler = null;
        }
    }

    protected override async Task TransportWriteAsync(byte[] packet, CancellationToken ct)
    {
        if (_charWriteV3 == 0)
            throw new InvalidOperationException("no write char");
        var buf = WinRt.CreateBuffer(packet);
        var ch3 = WinRt.Wrap<ICharacteristic3>(_charWriteV3);
        // Prefer WriteWithoutResponse (fast); fall back to WithResponse like pc_test.
        var st = await WriteWithOptionAsync(ch3, buf, GattWriteOption.WithoutResponse, ct);
        if (st != GattStatus.Success)
            st = await WriteWithOptionAsync(ch3, buf, GattWriteOption.WithResponse, ct);
        if (st != GattStatus.Success)
            throw new InvalidOperationException($"ATT write failed: status {st} ({packet.Length}B)");
    }

    private static async Task<GattStatus> WriteWithOptionAsync(
        ICharacteristic3 ch3, nint buf, GattWriteOption opt, CancellationToken ct)
    {
        var op = ch3.WriteValueWithResultAsyncWithOption(buf, (int)opt);
        await WinRt.WaitOpAsync(op, WinRtIids.OpWrite, "WriteValue", TimeSpan.FromSeconds(10), ct);
        var res = WinRt.Wrap<IWriteResult>(WinRt.Wrap<IOpWrite>(op).GetResults());
        return (GattStatus)res.GetStatus();
    }

    private void OnValueChanged(nint argsPtr)
    {
        try
        {
            var args = WinRt.Wrap<IValueChangedArgs>(argsPtr);
            var data = WinRt.ReadBuffer(args.GetCharacteristicValue());
            if (data.Length == 0)
                return;
            FeedReceived(data);
        }
        catch { /* never let native see managed exceptions */ }
    }

    protected override void SendAck(byte[] packet)
    {
        try
        {
            if (_charWriteV3 == 0)
                return;
            var buf = WinRt.CreateBuffer(packet);
            var ch3 = WinRt.Wrap<ICharacteristic3>(_charWriteV3);
            WriteWithOptionAsync(ch3, buf, GattWriteOption.WithResponse, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch { /* best effort on the notify thread */ }
    }

    protected override void DisposeTransport()
    {
        if (_charReadV1 != 0 && _vcCcw != 0)
        {
            try { WinRt.Wrap<ICharacteristic1>(_charReadV1).RemoveValueChanged(_vcReadToken); } catch { }
        }
        if (_vcWriteSubscribed && _charWriteV1 != 0 && _vcCcw != 0)
        {
            try { WinRt.Wrap<ICharacteristic1>(_charWriteV1).RemoveValueChanged(_vcWriteToken); } catch { }
            _vcWriteSubscribed = false;
        }
        if (_vcCcw != 0)
        {
            try { System.Runtime.InteropServices.Marshal.Release(_vcCcw); } catch { }
            _vcCcw = 0;
        }
        _vcHandler = null;
        if (_deviceObj != 0)
        {
            try { WinRt.QueryWrap<IWinRtClosable>(_deviceObj, WinRtIids.IClosable).Close(); } catch { }
            _deviceObj = 0;
        }
        _charReadV1 = _charWriteV1 = _charWriteV3 = 0;
    }

    /// <summary>Proves the WinRT BLE stack is reachable (factory activates).</summary>
    public static string ProbeStack()
    {
        try
        {
            var f = WinRt.GetFactory(
                "Windows.Devices.Bluetooth.BluetoothLEDevice", new Guid(WinRtIids.DeviceStatics));
            WinRt.AssertIid(f, WinRtIids.DeviceStatics, "IBluetoothLEDeviceStatics");
            return "reachable";
        }
        catch (Exception ex)
        {
            return $"unreachable ({ex.Message})";
        }
    }
}
