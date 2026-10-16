// Huami/Zepp OS BLE link over raw WinRT (FEE1 + 3512), counterpart to WinRtBandClient (FE95).
// Gadgetbridge: HuamiService 0000fee1 / 00001530, chars 00000016/00000017, endpoint 0x001e (ZeppOsNotificationService).

namespace BandBuzzLite.Band;

internal sealed class WinRtHuamiClient : ZeppOsSession
{
    private EventRegistrationToken _rxToken;
    private RecvHandler? _rxHandler;
    private nint _rxCcw;

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
        _encoder.Mtu = Mtu; _decoder.SetEncryptionParameters(Array.Empty<byte>());

        var addr = HuamiProtocol.ParseMac(_mac);
        await WaitForAdvertisementAsync(addr, TimeSpan.FromSeconds(20), ct);

        _log($"Connecting (Huami FEE1) to {_mac} ... (band takes 1 connection - close phone app first)");
        var statics = WinRt.Wrap<IDeviceStatics>(WinRt.GetFactory("Windows.Devices.Bluetooth.BluetoothLEDevice", new Guid(WinRtIids.DeviceStatics)));
        var op = statics.FromBluetoothAddressAsync(addr);
        await WinRt.WaitOpAsync(op, WinRtIids.OpDevice, "FromBluetoothAddressAsync", TimeSpan.FromSeconds(20), ct);
        _deviceObj = WinRt.Wrap<IOpDevice>(op).GetResults();
        if (_deviceObj == 0) throw new InvalidOperationException($"BLE device not found: {_mac}");

        var dev3 = WinRt.QueryWrap<IDevice3>(_deviceObj, WinRtIids.Device3);
        var svcOp = dev3.GetGattServicesAsync();
        await WinRt.WaitOpAsync(svcOp, WinRtIids.OpServices, "GetGattServicesAsync", TimeSpan.FromSeconds(15), ct);
        var svcRes = WinRt.Wrap<IServicesResult>(WinRt.Wrap<IOpServices>(svcOp).GetResults());
        if ((GattStatus)svcRes.GetStatus() != GattStatus.Success) throw new InvalidOperationException($"GATT discovery failed: {(GattStatus)svcRes.GetStatus()}");

        var svcVec = WinRt.QueryWrap<IVecServices>(svcRes.GetServices(), WinRtIids.VecServices);
        var seenSvcs = new List<Guid>();
        for (uint i = 0; i < svcVec.GetSize(); i++)
        {
            svcVec.GetAt(i, out var svcPtr);
            seenSvcs.Add(WinRt.QueryWrap<IService1>(svcPtr, WinRtIids.Service1).GetUuid());
        }
        _log($"GATT services found: {string.Join(", ", seenSvcs)}");
        var hasFee1 = seenSvcs.Contains(HuamiProtocol.ServiceFee1);
        var has3512 = seenSvcs.Contains(HuamiProtocol.ServiceFtg);
        if (!hasFee1 && !has3512)
            throw new InvalidOperationException($"Huami service FEE1/3512 not found (seen {string.Join(", ", seenSvcs)}). Not a Zepp OS device – try --family xiaomi for FE95 bands (7 Pro/8 Active). Band MAC {_mac} services: {string.Join(", ", seenSvcs)}");

        // Brute-force all services for 0x0016/0x0017 – they live under 00001530 on Zepp OS but search all to tolerate variants
        var allSeenChars = new List<Guid>();
        for (uint si = 0; si < svcVec.GetSize(); si++)
        {
            svcVec.GetAt(si, out var svcPtr);
            var svc3x = WinRt.QueryWrap<IService3>(svcPtr, WinRtIids.Service3);
            var chOpx = svc3x.GetCharacteristicsAsync();
            await WinRt.WaitOpAsync(chOpx, WinRtIids.OpCharacteristics, "GetCharacteristicsAsync", TimeSpan.FromSeconds(10), ct);
            var chResx = WinRt.Wrap<ICharacteristicsResult>(WinRt.Wrap<IOpCharacteristics>(chOpx).GetResults());
            if ((GattStatus)chResx.GetStatus() != GattStatus.Success) continue;
            var chVecx = WinRt.QueryWrap<IVecCharacteristics>(chResx.GetCharacteristics(), WinRtIids.VecCharacteristics);
            for (uint ci = 0; ci < chVecx.GetSize(); ci++)
            {
                chVecx.GetAt(ci, out var chPtr);
                var uuid = WinRt.QueryWrap<ICharacteristic1>(chPtr, WinRtIids.Characteristic1).GetUuid();
                allSeenChars.Add(uuid);
                if (uuid == HuamiProtocol.CharChunkedWrite2021) { _charWrite2021 = chPtr; _charWrite2021V3 = WinRt.Query(chPtr, new Guid(WinRtIids.Characteristic3)); }
                if (uuid == HuamiProtocol.CharChunkedRead2021) _charRead2021 = chPtr;
            }
            if (_charWrite2021 != 0 && _charRead2021 != 0) break;
        }
        _log($"GATT chars seen: {string.Join(", ", allSeenChars.Distinct())}");
        if (_charWrite2021 == 0)
            throw new InvalidOperationException($"Huami 0x0016 write char missing (seen chars: {string.Join(", ", allSeenChars.Distinct())}). If band is Xiaomi FE95 (7 Pro/8 Active) use --family xiaomi.");
        if (_charRead2021 == 0)
            throw new InvalidOperationException($"Huami 0x0017 notify char missing (seen chars: {string.Join(", ", allSeenChars.Distinct())}).");

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

    private async Task WaitForAdvertisementAsync(ulong addr, TimeSpan timeout, CancellationToken ct)
    {
        _log("Scanning for Huami band (FEE1)...");
        var watcherObj = WinRt.Activate("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher");
        var watcher = WinRt.QueryWrap<IAdvertWatcher>(watcherObj, WinRtIids.Watcher);
        watcher.SetScanningMode(1);
        var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _rxHandler = new RecvHandler(argsPtr => { try { var args = WinRt.Wrap<IReceivedArgs>(argsPtr); if (args.GetBluetoothAddress() == addr) seen.TrySetResult(true); } catch { } });
        _rxCcw = WinRt.ComInterfaceFor(_rxHandler, WinRtIids.RecvHandler);
        _rxToken = watcher.AddReceived(_rxCcw);
        try { watcher.Start(); } catch (Exception ex) { throw new InvalidOperationException($"BLE scan failed: {ex.Message}", ex); }
        try { if (!await AwaitOrThrowAsync(seen.Task, timeout, $"no BLE advertisement from {_mac}", ct)) throw new InvalidOperationException("BLE scan stopped"); }
        finally { try { watcher.Stop(); } catch { } try { watcher.RemoveReceived(_rxToken); } catch { } if (_rxCcw != 0) { System.Runtime.InteropServices.Marshal.Release(_rxCcw); _rxCcw = 0; } _rxHandler = null; }
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
