// Huami/Zepp OS BLE link over raw Win32 GATT (FEE1/3512, chunked-2021, endpoint 0x001e).

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Bluetooth;

namespace BandBuzzLite.Band;

internal sealed class Win32HuamiClient : ZeppOsSession
{
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _handle;
    private BTH_LE_GATT_CHARACTERISTIC? _read17;
    private BTH_LE_GATT_CHARACTERISTIC? _write16;
    private readonly List<nint> _eventHandles = new();
    private readonly List<nint> _regBuffers = new();
    private readonly PFNBLUETOOTH_GATT_EVENT_CALLBACK _callback;

    public unsafe Win32HuamiClient(string mac, string authKeyHex, Action<string>? log = null) : base(mac, authKeyHex, log) { _callback = OnGattEvent; }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposeTransport(); IsReady = false; _sessionKey = null;
        var target = Win32Gatt.NormalizeMac(_mac);
        var cands = Win32Gatt.EnumBleInterfaces();
        var pick = PickExact(cands, target);
        if (pick is null)
        {
            _log("Huami band not in LE list, inquiry-scanning (~15s)...");
            Win32Gatt.InquiryScanFor(HuamiProtocol.ParseMac(_mac), TimeSpan.FromSeconds(15), _log);
            cands = Win32Gatt.EnumBleInterfaces(); pick = PickExact(cands, target);
        }
        Exception? last = null;
        if (pick is not null) { try { OpenAndInit(pick.DevicePath, ct); await RunAuthAsync(ct); return; } catch (Exception ex) { last = ex; DisposeTransport(); } }
        else
        {
            foreach (var c in cands) { ct.ThrowIfCancellationRequested(); try { _log($"Probing Huami {c.AddressProp ?? c.DevicePath} for FEE1/3512..."); OpenAndInit(c.DevicePath, ct); await RunAuthAsync(ct); return; } catch (Exception ex) { last = ex; DisposeTransport(); } }
        }
        throw new InvalidOperationException($"Huami BLE device not found: {_mac} (band nearby?). Last: {last?.Message}");
    }
    private static Win32Gatt.BleCandidate? PickExact(List<Win32Gatt.BleCandidate> cands, string target) => cands.FirstOrDefault(c => (c.AddressProp is not null && Win32Gatt.NormalizeMac(c.AddressProp) == target) || c.DevicePath.Contains(target, StringComparison.OrdinalIgnoreCase));
    private void OpenAndInit(string devicePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _log($"Connecting (Huami) to {_mac} ...");
        _handle = Win32Gatt.OpenDevice(devicePath);
        var svcs = Win32Gatt.GetServices(_handle);
        _log($"Services: {string.Join(", ", svcs.Select(s => s.ServiceUuid.ToString()))}");
        var hasFee1 = svcs.Any(s => Win32Gatt.UuidMatches(s.ServiceUuid, HuamiProtocol.ServiceFee1));
        var has3512 = svcs.Any(s => Win32Gatt.UuidMatches(s.ServiceUuid, HuamiProtocol.ServiceFtg));
        if (!hasFee1 && !has3512) throw new InvalidOperationException($"Huami service FEE1/3512 not found (services: {string.Join(", ", svcs.Select(s => s.ServiceUuid))}). Try --family xiaomi for FE95.");
        // Search all services for 0x0016/0x0017 – not just one (they live under 00001530)
        var allChars = new List<BTH_LE_GATT_CHARACTERISTIC>();
        foreach (var s in svcs)
        {
            var cs = Win32Gatt.GetCharacteristics(_handle, s);
            allChars.AddRange(cs);
            foreach (var c in cs)
            {
                if (Win32Gatt.UuidMatches(c.CharacteristicUuid, HuamiProtocol.CharChunkedWrite2021)) _write16 = c;
                if (Win32Gatt.UuidMatches(c.CharacteristicUuid, HuamiProtocol.CharChunkedRead2021)) _read17 = c;
            }
            if (_write16 != null && _read17 != null) break;
        }
        _log($"Chars seen: {string.Join(", ", allChars.Select(c => c.CharacteristicUuid.ToString()))}");
        if (_write16 is null) throw new InvalidOperationException($"Huami 0x0016 write char missing (chars: {string.Join(", ", allChars.Select(c => c.CharacteristicUuid))}). Try --family xiaomi for FE95 bands (7 Pro/8 Active).");
        if (_read17 is null) throw new InvalidOperationException($"Huami 0x0017 notify char missing (chars: {string.Join(", ", allChars.Select(c => c.CharacteristicUuid))}).");
        EnableNotify(_read17.Value, ct);
        _log("Huami connected, notify on 0x0017.");
    }
    private unsafe void EnableNotify(BTH_LE_GATT_CHARACTERISTIC c, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_handle is null) throw new InvalidOperationException("no handle");
        var descs = Win32Gatt.GetDescriptors(_handle, c);
        var cccd = descs.FirstOrDefault(d => d.DescriptorType == BTH_LE_GATT_DESCRIPTOR_TYPE.ClientCharacteristicConfiguration);
        if (cccd.AttributeHandle == 0) throw new InvalidOperationException($"CCCD missing {c.AttributeHandle:X4}");
        var value = new BTH_LE_GATT_DESCRIPTOR_VALUE { DescriptorType = BTH_LE_GATT_DESCRIPTOR_TYPE.ClientCharacteristicConfiguration };
        value.ClientCharacteristicConfiguration.IsSubscribeToNotification = true;
        Win32Gatt.ThrowIfFailed(PInvoke.BluetoothGATTSetDescriptorValue(_handle, cccd, &value, PInvoke.BLUETOOTH_GATT_FLAG_NONE), $"CCCD {c.AttributeHandle:X4}");
        var size = BLUETOOTH_GATT_VALUE_CHANGED_EVENT_REGISTRATION.SizeOf(1);
        var mem = Marshal.AllocHGlobal(size);
        try { *(ushort*)mem = 1; var off = Marshal.OffsetOf<BLUETOOTH_GATT_VALUE_CHANGED_EVENT_REGISTRATION>("Characteristics").ToInt64(); *(BTH_LE_GATT_CHARACTERISTIC*)(mem + off) = c; Win32Gatt.ThrowIfFailed(PInvoke.BluetoothGATTRegisterEvent(_handle, BTH_LE_GATT_EVENT_TYPE.CharacteristicValueChangedEvent, (void*)mem, _callback, null, out var ev, PInvoke.BLUETOOTH_GATT_FLAG_NONE), $"register {c.AttributeHandle:X4}"); _eventHandles.Add(ev); _regBuffers.Add(mem); mem = 0; } finally { if (mem != 0) Marshal.FreeHGlobal(mem); }
    }
    protected override Task TransportWriteAsync(byte[] chunk, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (_handle is null || _write16 is null) throw new InvalidOperationException("no write char"); var hr = Win32Gatt.WriteCharacteristic(_handle, _write16.Value, chunk, withoutResponse: true); if (hr.Failed) hr = Win32Gatt.WriteCharacteristic(_handle, _write16.Value, chunk, withoutResponse: false); if (hr.Failed) throw new InvalidOperationException($"ATT write failed: {hr} ({chunk.Length}B)"); return Task.CompletedTask; }
    private unsafe void OnGattEvent(BTH_LE_GATT_EVENT_TYPE type, void* outParam, void* context)
    {
        if (type != BTH_LE_GATT_EVENT_TYPE.CharacteristicValueChangedEvent || outParam is null) return;
        var ev = (BLUETOOTH_GATT_VALUE_CHANGED_EVENT*)outParam; var val = ev->CharacteristicValue; if (val is null) return; var len = val->DataSize; if (len == 0 || len > 4096) return; if ((nuint)len > ev->CharacteristicValueDataSize) len = (uint)ev->CharacteristicValueDataSize; var data = new byte[len]; var off = Marshal.OffsetOf<BTH_LE_GATT_CHARACTERISTIC_VALUE>("Data").ToInt64(); Marshal.Copy((nint)val + (int)off, data, 0, (int)len); FeedReceived(data);
    }
    protected override void SendChunkedAck(byte handle, byte count) { try { if (_handle is null || _handle.IsInvalid || _write16 is null) return; var ack = HuamiFrame.BuildChunkedAck(handle, count); Win32Gatt.WriteCharacteristic(_handle, _write16.Value, ack, withoutResponse: true); } catch { } }
    protected override void DisposeTransport()
    {
        foreach (var ev in _eventHandles) try { PInvoke.BluetoothGATTUnregisterEvent(ev, PInvoke.BLUETOOTH_GATT_FLAG_NONE); } catch { }
        _eventHandles.Clear(); foreach (var mem in _regBuffers) Marshal.FreeHGlobal(mem); _regBuffers.Clear(); _read17 = null; _write16 = null; _handle?.Dispose(); _handle = null;
    }
}
