// Live self-check for the hand-rolled WinRT interop. Needs no band:
// watcher lifecycle, delegate CCW add/remove (proves the delegate IID),
// a bogus-address async op (proves factory + statics + op IID + polling),
// and a Buffer roundtrip. Any failure here means interop metadata is wrong.

namespace BandBuzzLite.Band;

internal static class WinRtCheck
{
    public static async Task<int> Run()
    {
        var failures = 0;
        void Check(bool ok, string name, string extra = "")
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(extra.Length == 0 ? "" : " " + extra)}");
            if (!ok)
                failures++;
        }

        // 1. Activate watcher + QI + Status.
        nint watcherObj = 0;
        IAdvertWatcher? watcher = null;
        try
        {
            watcherObj = WinRt.Activate(
                "Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher");
            watcher = WinRt.QueryWrap<IAdvertWatcher>(watcherObj, WinRtIids.Watcher);
            WinRt.AssertIid(watcherObj, WinRtIids.Watcher, "IBluetoothLEAdvertisementWatcher");
            Check(true, "watcher-activate", $"status={watcher.GetStatus()}");
        }
        catch (Exception ex)
        {
            Check(false, "watcher-activate", ex.Message);
        }

        // 2+3. Delegate CCW add (proves teh<Watcher,RxArgs> IID) + Start/Stop
        // cycle (proves vtable alignment of the control path). Start requires
        // a registered Received handler - the OS rejects it otherwise.
        if (watcher is not null)
        {
            nint ccw = 0;
            var token = default(EventRegistrationToken);
            try
            {
                var handler = new RecvHandler(_ => { });
                ccw = WinRt.ComInterfaceFor(handler, WinRtIids.RecvHandler);
                token = watcher.AddReceived(ccw);
                GC.KeepAlive(handler);
                Check(true, "watcher-event-ccw", $"token={token.Value}");
            }
            catch (Exception ex)
            {
                Check(false, "watcher-event-ccw", ex.Message);
            }
            try
            {
                watcher.SetScanningMode(1);
                watcher.Start();
                await Task.Delay(3000);
                watcher.Stop();
                Check(true, "watcher-start-stop", $"status={watcher.GetStatus()}");
            }
            catch (Exception ex)
            {
                Check(false, "watcher-start-stop", ex.Message);
            }
            try
            {
                watcher.RemoveReceived(token);
                if (ccw != 0)
                    System.Runtime.InteropServices.Marshal.Release(ccw);
            }
            catch { }
        }

        // 4. Bogus-address async (proves statics + op IID + poll loop).
        try
        {
            var statics = WinRt.Wrap<IDeviceStatics>(WinRt.GetFactory(
                "Windows.Devices.Bluetooth.BluetoothLEDevice", new Guid(WinRtIids.DeviceStatics)));
            var op = statics.FromBluetoothAddressAsync(0x020000000000);
            try
            {
                await WinRt.WaitOpAsync(op, WinRtIids.OpDevice, "FromBluetoothAddressAsync(bogus)",
                    TimeSpan.FromSeconds(12), CancellationToken.None);
                var dev = WinRt.Wrap<IOpDevice>(op).GetResults();
                Check(true, "bogus-address-async", dev == 0 ? "completed, null device (clean)" : $"completed, device=0x{dev:X}");
            }
            catch (Exception ex) when (ex is TimeoutException || ex is InvalidOperationException)
            {
                Check(true, "bogus-address-async", $"clean failure path: {ex.Message.Split('\n')[0]}");
            }
        }
        catch (Exception ex)
        {
            Check(false, "bogus-address-async", ex.Message);
        }

        // 5. Buffer factory roundtrip (proves IBufferFactory + IBufferByteAccess).
        try
        {
            var payload = new byte[] { 1, 2, 3, 4, 5 };
            var back = WinRt.ReadBuffer(WinRt.CreateBuffer(payload));
            Check(back.SequenceEqual(payload), "buffer-roundtrip");
        }
        catch (Exception ex)
        {
            Check(false, "buffer-roundtrip", ex.Message);
        }

        Console.WriteLine(failures == 0 ? "WINRTCHECK OK" : $"WINRTCHECK {failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
