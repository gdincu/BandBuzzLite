// Huami/Zepp OS BLE link over raw WinRT (FEE1 + 3512), counterpart to WinRtBandClient (FE95).
// Gadgetbridge: HuamiService 0000fee1 / 00001530, chars 00000016/00000017, endpoint 0x001e (ZeppOsNotificationService).

namespace BandBuzzLite.Band;

internal sealed class WinRtHuamiClient : ZeppOsSession
{
    private nint _deviceObj;
    private nint _charWrite2021; // 0x0016
    private nint _charWrite2021V3;
    private nint _charRead2021; // 0x0017 notify
    private EventRegistrationToken _vcToken;
    private ValueHandler? _vcHandler;
    private nint _vcCcw;

    public WinRtHuamiClient(string mac, string authKeyHex, Action<string>? log = null) : base(mac, authKeyHex, log) { }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposeTransport();
        IsReady = false;
        _sessionKey = null;
        _encoder.Mtu = Mtu;

        var addr = XiaomiProtocol.ParseMac(_mac);
        await BleDiscovery.WaitForAdvertisementAsync(addr, _mac, TimeSpan.FromSeconds(20), ct, _log);

        _log($"Connecting (Huami FEE1) to {_mac} ... (band takes 1 connection - close phone app first)");
        var statics = WinRt.Wrap<IDeviceStatics>(WinRt.GetFactory("Windows.Devices.Bluetooth.BluetoothLEDevice", new Guid(WinRtIids.DeviceStatics)));
        var op = statics.FromBluetoothAddressAsync(addr);
        await WinRt.WaitOpAsync(op, WinRtIids.OpDevice, "FromBluetoothAddressAsync", TimeSpan.FromSeconds(20), ct);
        _deviceObj = WinRt.Wrap<IOpDevice>(op).GetResults();
        if (_deviceObj == 0) throw new InvalidOperationException($"BLE device not found: {_mac}");

        var (w, w3, r) = await BleDiscovery.FindHuamiCharsAsync(_deviceObj, ct, _log);
        _charWrite2021 = w; _charWrite2021V3 = w3; _charRead2021 = r;

        _vcHandler = new ValueHandler(OnValueChanged);
        _vcCcw = WinRt.ComInterfaceFor(_vcHandler, WinRtIids.ValueHandler);
        Subscribe(_charRead2021, ref _vcToken, ct);
        _log("Huami connected, chunked-2021 notify on 0x0017.");

        await RunAuthAsync(ct);
    }

    private void Subscribe(nint charV1, ref EventRegistrationToken token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ch3 = WinRt.QueryWrap<ICharacteristic3>(charV1, WinRtIids.Characteristic3);
        var cccdOp = ch3.WriteCccdWithResultAsync((int)CccdValue.Notify);
        WinRt.WaitOpAsync(cccdOp, WinRtIids.OpWrite, "WriteCCCD", TimeSpan.FromSeconds(10), ct).GetAwaiter().GetResult();
        var st = WinRt.Wrap<IWriteResult>(WinRt.Wrap<IOpWrite>(cccdOp).GetResults()).GetStatus();
        if ((GattStatus)st != GattStatus.Success) throw new InvalidOperationException($"CCCD write failed: {(GattStatus)st}");
        token = WinRt.Wrap<ICharacteristic1>(charV1).AddValueChanged(_vcCcw);
    }

    protected override Task TransportWriteAsync(byte[] chunk, CancellationToken ct)
    {
        if (_charWrite2021V3 == 0) throw new InvalidOperationException("no write char");
        var buf = WinRt.CreateBuffer(chunk);
        var ch3 = WinRt.Wrap<ICharacteristic3>(_charWrite2021V3);
        return WriteWithOptionAsync(ch3, buf, GattWriteOption.WithoutResponse, ct).ContinueWith(async t =>
        {
            var st = await t;
            if (st != GattStatus.Success) { var st2 = await WriteWithOptionAsync(ch3, buf, GattWriteOption.WithResponse, ct); if (st2 != GattStatus.Success) throw new InvalidOperationException($"ATT write failed: {st2} ({chunk.Length}B)"); }
        }, ct).Unwrap();
    }
    private static async Task<GattStatus> WriteWithOptionAsync(ICharacteristic3 ch3, nint buf, GattWriteOption opt, CancellationToken ct)
    {
        var op = ch3.WriteValueWithResultAsyncWithOption(buf, (int)opt);
        await WinRt.WaitOpAsync(op, WinRtIids.OpWrite, "WriteValue", TimeSpan.FromSeconds(10), ct);
        return (GattStatus)WinRt.Wrap<IWriteResult>(WinRt.Wrap<IOpWrite>(op).GetResults()).GetStatus();
    }
    private void OnValueChanged(nint argsPtr) { try { var args = WinRt.Wrap<IValueChangedArgs>(argsPtr); var data = WinRt.ReadBuffer(args.GetCharacteristicValue()); if (data.Length != 0) FeedReceived(data); } catch { } }
    protected override void SendChunkedAck(byte handle, byte count) { try { var ack = HuamiFrame.BuildChunkedAck(handle, count); var buf = WinRt.CreateBuffer(ack); var ch3 = WinRt.Wrap<ICharacteristic3>(_charWrite2021V3); WriteWithOptionAsync(ch3, buf, GattWriteOption.WithoutResponse, CancellationToken.None).GetAwaiter().GetResult(); } catch { } }
    protected override void DisposeTransport()
    {
        if (_charRead2021 != 0 && _vcCcw != 0) try { WinRt.Wrap<ICharacteristic1>(_charRead2021).RemoveValueChanged(_vcToken); } catch { }
        if (_vcCcw != 0) try { System.Runtime.InteropServices.Marshal.Release(_vcCcw); } catch { } _vcCcw = 0; _vcHandler = null;
        if (_deviceObj != 0) try { WinRt.QueryWrap<IWinRtClosable>(_deviceObj, WinRtIids.IClosable).Close(); } catch { } _deviceObj = 0; _charWrite2021 = _charWrite2021V3 = _charRead2021 = 0;
    }
    public static string ProbeStack() => WinRtBandClient.ProbeStack();
}
