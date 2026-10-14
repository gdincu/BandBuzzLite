// Xiaomi BLE link over raw Win32 GATT (no WinRT): SetupDi enumeration,
// CreateFile + BluetoothGATT*, notify via BluetoothGATTRegisterEvent.
// Only reaches bands Windows already lists (paired/remembered); the WinRT
// transport in WinRtBandClient.cs connects from raw advertisements instead.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Bluetooth;

namespace BandBuzzLite.Band;

internal sealed class Win32BandClient : XiaomiSession
{
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _handle;
    private BTH_LE_GATT_CHARACTERISTIC? _read;
    private BTH_LE_GATT_CHARACTERISTIC? _write;
    private readonly List<nint> _eventHandles = new();
    private readonly List<nint> _regBuffers = new();
    private readonly PFNBLUETOOTH_GATT_EVENT_CALLBACK _callback;

    public unsafe Win32BandClient(string mac, string authKeyHex, Action<string>? log = null)
        : base(mac, authKeyHex, log)
    {
        _callback = OnGattEvent;
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposeTransport();
        IsReady = false;
        _keys = null;
        _encIndex = 1;

        var target = Win32Gatt.NormalizeMac(_mac);
        var cands = Win32Gatt.EnumBleInterfaces();
        var pick = PickExact(cands, target);
        if (pick is null)
        {
            _log("Band not in the LE device list, inquiry-scanning (~15s)...");
            Win32Gatt.InquiryScanFor(XiaomiProtocol.ParseMac(_mac), TimeSpan.FromSeconds(15), _log);
            cands = Win32Gatt.EnumBleInterfaces();
            pick = PickExact(cands, target);
        }

        Exception? last = null;
        if (pick is not null)
        {
            try
            {
                OpenAndInit(pick.DevicePath, ct);
                await RunAuthAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                DisposeTransport();
            }
        }
        else
        {
            // No MAC match: probe each known LE device for the FE95 service.
            foreach (var c in cands)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    _log($"Probing {c.AddressProp ?? c.DevicePath} for FE95...");
                    OpenAndInit(c.DevicePath, ct);
                    await RunAuthAsync(ct);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    DisposeTransport();
                }
            }
        }
        throw new InvalidOperationException(
            $"BLE device not found: {_mac} (band nearby and advertising?). " +
            $"If this is the first run, pair once via Windows Settings > Bluetooth, then retry." +
            (last is null ? "" : $" Last error: {last.Message}"));
    }

    private static Win32Gatt.BleCandidate? PickExact(List<Win32Gatt.BleCandidate> cands, string target) =>
        cands.FirstOrDefault(c =>
            (c.AddressProp is not null && Win32Gatt.NormalizeMac(c.AddressProp) == target) ||
            c.DevicePath.Contains(target, StringComparison.OrdinalIgnoreCase));

    private void OpenAndInit(string devicePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _log($"Connecting to {_mac} ... (band takes 1 connection - close phone app first)");
        _handle = Win32Gatt.OpenDevice(devicePath);

        var svcs = Win32Gatt.GetServices(_handle);
        var svc = svcs.FirstOrDefault(s => Win32Gatt.UuidMatches(s.ServiceUuid, XiaomiProtocol.ServiceFe95));
        if (svc.AttributeHandle == 0)
            throw new InvalidOperationException("FE95 service not found");

        var chars = Win32Gatt.GetCharacteristics(_handle, svc);
        _read = chars.FirstOrDefault(c => Win32Gatt.UuidMatches(c.CharacteristicUuid, XiaomiProtocol.CharCommandRead));
        _write = chars.FirstOrDefault(c => Win32Gatt.UuidMatches(c.CharacteristicUuid, XiaomiProtocol.CharCommandWrite));
        if (_read is null)
            throw new InvalidOperationException("0x51 characteristic missing");
        if (_write is null)
            throw new InvalidOperationException("0x52 characteristic missing");

        // Subscribe BEFORE any write, on both chars like the Android app + pc_test.
        EnableNotify(_read.Value, ct);
        try { EnableNotify(_write.Value, ct); } catch { /* some stacks refuse 0x52 notify */ }
        _log("Connected, notifications on.");
    }

    private unsafe void EnableNotify(BTH_LE_GATT_CHARACTERISTIC c, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_handle is null)
            throw new InvalidOperationException("no device handle");
        var descs = Win32Gatt.GetDescriptors(_handle, c);
        var cccd = descs.FirstOrDefault(d =>
            d.DescriptorType == BTH_LE_GATT_DESCRIPTOR_TYPE.ClientCharacteristicConfiguration);
        if (cccd.AttributeHandle == 0)
            throw new InvalidOperationException($"CCCD missing for char {c.AttributeHandle:X4}");
        var value = new BTH_LE_GATT_DESCRIPTOR_VALUE
        {
            DescriptorType = BTH_LE_GATT_DESCRIPTOR_TYPE.ClientCharacteristicConfiguration,
        };
        value.ClientCharacteristicConfiguration.IsSubscribeToNotification = true;
        Win32Gatt.ThrowIfFailed(PInvoke.BluetoothGATTSetDescriptorValue(
            _handle, cccd, &value, PInvoke.BLUETOOTH_GATT_FLAG_NONE), $"CCCD write ({c.AttributeHandle:X4})");

        var size = BLUETOOTH_GATT_VALUE_CHANGED_EVENT_REGISTRATION.SizeOf(1);
        var mem = Marshal.AllocHGlobal(size);
        try
        {
            *(ushort*)mem = 1;
            var off = Marshal.OffsetOf<BLUETOOTH_GATT_VALUE_CHANGED_EVENT_REGISTRATION>("Characteristics").ToInt64();
            *(BTH_LE_GATT_CHARACTERISTIC*)(mem + off) = c;
            Win32Gatt.ThrowIfFailed(PInvoke.BluetoothGATTRegisterEvent(
                _handle, BTH_LE_GATT_EVENT_TYPE.CharacteristicValueChangedEvent,
                (void*)mem, _callback, null, out var ev, PInvoke.BLUETOOTH_GATT_FLAG_NONE),
                $"event register ({c.AttributeHandle:X4})");
            _eventHandles.Add(ev);
            _regBuffers.Add(mem);
            mem = 0; // owned by the session now; freed on dispose
        }
        finally
        {
            if (mem != 0)
                Marshal.FreeHGlobal(mem);
        }
    }

    protected override Task TransportWriteAsync(byte[] packet, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_handle is null || _write is null)
            throw new InvalidOperationException("no write char");
        // Prefer WriteWithoutResponse (fast); fall back to WithResponse like pc_test.
        var hr = Win32Gatt.WriteCharacteristic(_handle, _write.Value, packet, withoutResponse: true);
        if (hr.Failed)
            hr = Win32Gatt.WriteCharacteristic(_handle, _write.Value, packet, withoutResponse: false);
        if (hr.Failed)
            throw new InvalidOperationException(
                $"ATT write failed: {hr} (0x{hr.Value:X8}, {packet.Length}B)");
        return Task.CompletedTask;
    }

    private unsafe void OnGattEvent(BTH_LE_GATT_EVENT_TYPE type, void* outParam, void* context)
    {
        if (type != BTH_LE_GATT_EVENT_TYPE.CharacteristicValueChangedEvent || outParam is null)
            return;
        var ev = (BLUETOOTH_GATT_VALUE_CHANGED_EVENT*)outParam;
        var val = ev->CharacteristicValue;
        if (val is null)
            return;
        var len = val->DataSize;
        if (len == 0 || len > 4096)
            return;
        if ((nuint)len > ev->CharacteristicValueDataSize)
            len = (uint)ev->CharacteristicValueDataSize;
        var data = new byte[len];
        var off = Marshal.OffsetOf<BTH_LE_GATT_CHARACTERISTIC_VALUE>("Data").ToInt64();
        Marshal.Copy((nint)val + (int)off, data, 0, (int)len);
        FeedReceived(data);
    }

    protected override void SendAck(byte[] packet)
    {
        try
        {
            if (_handle is null || _handle.IsInvalid || _write is null)
                return;
            Win32Gatt.WriteCharacteristic(_handle, _write.Value, packet, withoutResponse: true);
        }
        catch { /* best effort on the notify thread */ }
    }

    protected override void DisposeTransport()
    {
        foreach (var ev in _eventHandles)
        {
            try { PInvoke.BluetoothGATTUnregisterEvent(ev, PInvoke.BLUETOOTH_GATT_FLAG_NONE); }
            catch { /* best effort */ }
        }
        _eventHandles.Clear();
        foreach (var mem in _regBuffers)
            Marshal.FreeHGlobal(mem);
        _regBuffers.Clear();
        _read = null;
        _write = null;
        _handle?.Dispose();
        _handle = null;
    }
}
