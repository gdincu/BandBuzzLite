namespace BandBuzzLite.Band;

/// <summary>Shared GATT discovery helpers for FE95 (Xiaomi) and FEE1/3512 (Huami). De-duplicates WinRt/Win32 brute-force loops.</summary>
internal static class BleDiscovery
{
    public static async Task<(nint writeV1, nint writeV3, nint readV1)> FindXiaomiCharsAsync(nint deviceObj, CancellationToken ct, Action<string> log)
    {
        var dev3 = WinRt.QueryWrap<IDevice3>(deviceObj, WinRtIids.Device3);
        var svcOp = dev3.GetGattServicesAsync();
        await WinRt.WaitOpAsync(svcOp, WinRtIids.OpServices, "GetGattServicesAsync", TimeSpan.FromSeconds(15), ct);
        var svcRes = WinRt.Wrap<IServicesResult>(WinRt.Wrap<IOpServices>(svcOp).GetResults());
        if ((GattStatus)svcRes.GetStatus() != GattStatus.Success) throw new InvalidOperationException($"GATT discovery failed: {(GattStatus)svcRes.GetStatus()}");
        var svcVec = WinRt.QueryWrap<IVecServices>(svcRes.GetServices(), WinRtIids.VecServices);
        var seen = new List<Guid>();
        nint? fe95 = null;
        for (uint i = 0; i < svcVec.GetSize(); i++) { svcVec.GetAt(i, out var p); var u = WinRt.QueryWrap<IService1>(p, WinRtIids.Service1).GetUuid(); seen.Add(u); if (u == XiaomiProtocol.ServiceFe95) fe95 = p; }
        if (fe95 is null) throw new InvalidOperationException($"FE95 service not found (seen {string.Join(", ", seen)}). Try --family huami for FEE1/3512 bands.");
        var svc3 = WinRt.QueryWrap<IService3>(fe95.Value, WinRtIids.Service3);
        var chOp = svc3.GetCharacteristicsAsync();
        await WinRt.WaitOpAsync(chOp, WinRtIids.OpCharacteristics, "GetCharacteristicsAsync", TimeSpan.FromSeconds(15), ct);
        var chRes = WinRt.Wrap<ICharacteristicsResult>(WinRt.Wrap<IOpCharacteristics>(chOp).GetResults());
        if ((GattStatus)chRes.GetStatus() != GattStatus.Success) throw new InvalidOperationException($"Char discovery failed: {(GattStatus)chRes.GetStatus()}");
        var chVec = WinRt.QueryWrap<IVecCharacteristics>(chRes.GetCharacteristics(), WinRtIids.VecCharacteristics);
        nint r = 0, w = 0, w3 = 0;
        for (uint i = 0; i < chVec.GetSize(); i++) { chVec.GetAt(i, out var p); var u = WinRt.QueryWrap<ICharacteristic1>(p, WinRtIids.Characteristic1).GetUuid(); if (u == XiaomiProtocol.CharCommandRead) r = p; else if (u == XiaomiProtocol.CharCommandWrite) { w = p; w3 = WinRt.Query(p, new Guid(WinRtIids.Characteristic3)); } }
        if (r == 0) throw new InvalidOperationException("0x51 characteristic missing");
        if (w == 0) throw new InvalidOperationException("0x52 characteristic missing");
        return (w, w3, r);
    }

    public static async Task<(nint write, nint write3, nint read)> FindHuamiCharsAsync(nint deviceObj, CancellationToken ct, Action<string> log)
    {
        var dev3 = WinRt.QueryWrap<IDevice3>(deviceObj, WinRtIids.Device3);
        var svcOp = dev3.GetGattServicesAsync();
        await WinRt.WaitOpAsync(svcOp, WinRtIids.OpServices, "GetGattServicesAsync", TimeSpan.FromSeconds(15), ct);
        var svcRes = WinRt.Wrap<IServicesResult>(WinRt.Wrap<IOpServices>(svcOp).GetResults());
        if ((GattStatus)svcRes.GetStatus() != GattStatus.Success) throw new InvalidOperationException($"GATT discovery failed: {(GattStatus)svcRes.GetStatus()}");
        var svcVec = WinRt.QueryWrap<IVecServices>(svcRes.GetServices(), WinRtIids.VecServices);
        var seen = new List<Guid>();
        for (uint i = 0; i < svcVec.GetSize(); i++) { svcVec.GetAt(i, out var p); seen.Add(WinRt.QueryWrap<IService1>(p, WinRtIids.Service1).GetUuid()); }
        log($"GATT services found: {string.Join(", ", seen)}");
        if (!seen.Contains(HuamiProtocol.ServiceFee1) && !seen.Contains(HuamiProtocol.ServiceFtg))
            throw new InvalidOperationException($"Huami service FEE1/3512 not found (seen {string.Join(", ", seen)}). Try --family xiaomi.");
        var allChars = new List<Guid>(); nint w = 0, w3 = 0, r = 0;
        for (uint si = 0; si < svcVec.GetSize(); si++)
        {
            svcVec.GetAt(si, out var svcPtr);
            var svc3x = WinRt.QueryWrap<IService3>(svcPtr, WinRtIids.Service3);
            var chOpx = svc3x.GetCharacteristicsAsync();
            await WinRt.WaitOpAsync(chOpx, WinRtIids.OpCharacteristics, "GetCharacteristicsAsync", TimeSpan.FromSeconds(10), ct);
            var chResx = WinRt.Wrap<ICharacteristicsResult>(WinRt.Wrap<IOpCharacteristics>(chOpx).GetResults());
            if ((GattStatus)chResx.GetStatus() != GattStatus.Success) continue;
            var chVecx = WinRt.QueryWrap<IVecCharacteristics>(chResx.GetCharacteristics(), WinRtIids.VecCharacteristics);
            for (uint ci = 0; ci < chVecx.GetSize(); ci++) { chVecx.GetAt(ci, out var chPtr); var u = WinRt.QueryWrap<ICharacteristic1>(chPtr, WinRtIids.Characteristic1).GetUuid(); allChars.Add(u); if (u == HuamiProtocol.CharChunkedWrite2021) { w = chPtr; w3 = WinRt.Query(chPtr, new Guid(WinRtIids.Characteristic3)); } if (u == HuamiProtocol.CharChunkedRead2021) r = chPtr; }
            if (w != 0 && r != 0) break;
        }
        log($"GATT chars seen: {string.Join(", ", allChars.Distinct())}");
        if (w == 0) throw new InvalidOperationException($"Huami 0x0016 write char missing (seen chars: {string.Join(", ", allChars.Distinct())}). If band is Xiaomi FE95 use --family xiaomi.");
        if (r == 0) throw new InvalidOperationException($"Huami 0x0017 notify char missing (seen chars: {string.Join(", ", allChars.Distinct())}).");
        return (w, w3, r);
    }

    public static async Task WaitForAdvertisementAsync(ulong addr, string mac, TimeSpan timeout, CancellationToken ct, Action<string> log)
    {
        log("Scanning for the band...");
        var watcherObj = WinRt.Activate("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher");
        var watcher = WinRt.QueryWrap<IAdvertWatcher>(watcherObj, WinRtIids.Watcher);
        watcher.SetScanningMode(1);
        var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RecvHandler? rxHandler = null; nint rxCcw = 0; EventRegistrationToken rxToken = default;
        rxHandler = new RecvHandler(argsPtr => { try { var args = WinRt.Wrap<IReceivedArgs>(argsPtr); if (args.GetBluetoothAddress() == addr) seen.TrySetResult(true); } catch { } });
        rxCcw = WinRt.ComInterfaceFor(rxHandler, WinRtIids.RecvHandler);
        rxToken = watcher.AddReceived(rxCcw);
        try { watcher.Start(); } catch (Exception ex) { throw new InvalidOperationException($"BLE scan failed (radio off?): {ex.Message}", ex); }
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try { await seen.Task.WaitAsync(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"no BLE advertisement from {mac} (band nearby and advertising?)"); }
            if (!await seen.Task) throw new InvalidOperationException("BLE scan stopped unexpectedly (radio off?)");
        }
        finally { try { watcher.Stop(); } catch { } try { watcher.RemoveReceived(rxToken); } catch { } if (rxCcw != 0) System.Runtime.InteropServices.Marshal.Release(rxCcw); }
    }
}
