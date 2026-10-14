# BandBuzzLite

Sub-800KB notifier for the Xiaomi Smart Band 8 Active. Same card protocol
as [BandBuzz](../BandBuzz) (`XiaomiProto` / `XiaomiCrypto` / `XiaomiFrame`
are verbatim ports — `--selftest` vectors prove it), but with **zero
`Microsoft.Windows.SDK.NET.dll`**: the default transport is hand-rolled raw
WinRT interop (plain COM activation + ComWrappers source-gen, BCL only),
connecting straight from LE advertisements — so it reaches bands Windows
never listed. The BLE side needs no install step, no identity package, no
certificate; only toast watching does (see Use).

`dotnet publish -c Release -o publish` → ~600KB total
(`BandBuzzLite.exe` + `BandBuzzLite.dll`). Needs the .NET 9 runtime.

## Use

First, put your band's real MAC and auth key in `appsettings.json` next to
the exe (the file ships with placeholders; find the MAC with `--scan` or the
Mi Fitness app). One-shot cards then work unpackaged, straight after publish:

```powershell
dotnet publish src\BandBuzzLite\BandBuzzLite.csproj -c Release -o publish
.\publish\BandBuzzLite.exe --test "Hello" "It works"
```

Watch mode (the default) and `--list` need toast access, which Windows only
grants to the installed identity package — one command, safe to re-run.
Everything else works unpackaged:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
.\publish\BandBuzzLite.exe            # watch Chrome/Teams -> band
```

| What | Command |
|---|---|
| Watch and forward (the normal mode) | `BandBuzzLite` |
| See what Windows is showing right now | `BandBuzzLite --list` |
| Send a one-off test card (no install) | `BandBuzzLite --test "Title" "Body" [app]` |
| Hardware-free crypto/framing checks | `BandBuzzLite --selftest` |
| Interop self-test (no band needed) | `BandBuzzLite --winrtcheck` |
| Environment + access troubleshooter | `BandBuzzLite --diag` |
| List known Bluetooth devices | `BandBuzzLite --scan` |

Settings live in `appsettings.json` next to the exe (same shape as BandBuzz:
`Mac`, `AuthKey`, `MaxWrite`, …), overridable with
`--mac AA:BB:CC:DD:EE:FF --key <32-hex>`. Nothing is baked into the binary:
missing/placeholder config fails validation up front.

No pairing needed: the default transport scans for the band's
advertisements directly (same as the full BandBuzz app). A legacy
`--win32` flag keeps the raw Win32 GATT transport, which only sees
paired/remembered devices.

## Status

- ✅ builds with 0 warnings / 0 errors (`net9.0`, x64, CsWin32 0.3.346)
- ✅ `--selftest`, `--winrtcheck`, `--list` (live toasts), install + `--diag`
  all verified; every computed IID runtime-asserted via `GetIids`
- ⚠️ **end-to-end send against a real band is the one remaining check.**
  Run `--test`; if the GATT handshake misbehaves, the log pinpoints which
  primitive failed (every QI is asserted, every async op is named).

## How the raw WinRT interop works (and why to trust it)

`Band/WinRt*.cs` reimplements the 5% of the WinRT projection BandBuzz uses
— activate `BluetoothLEAdvertisementWatcher` / `BluetoothLEDevice`,
`IAsyncOperation` polling (no callbacks), `IBuffer` via the system Buffer
factory, `ValueChanged`/`Received` via two tiny CCW delegates — on top of
`RoGetActivationFactory`/`RoActivateInstance` + source-generated ComWrappers.
Interface IIDs and vtable orders were taken from `Windows.winmd`; the 8
generic-instantiation IIDs were computed with the `winrt/base.h` pinterface
algorithm and cross-checked 6/6 against .NET's own WinRT oracle, and every
computed IID is re-asserted at runtime via `GetIids` membership, so a wrong
IID fails fast instead of corrupting calls.

`--winrtcheck` exercises all of this live (activate, scan Start/Stop, CCW
add/remove, a bogus-address async op, Buffer roundtrip) with no band.

Known tradeoffs (documented in code, not hidden):

- Async WinRT ops are polled, not completed via callbacks (simpler, no
  extra CCWs; adds ≤25ms latency per op — irrelevant for a sender).
- COM refcounts from QI are left to GC/finalizers; event registrations,
  watcher and device are torn down explicitly via API. Fine for a
  short-lived sender process.
- x64-only v1 (the CsWin32 half has pointer-size-dependent layout).

## Layout

- `src/BandBuzzLite/Band/Xiaomi*.cs` — protocol, verbatim from BandBuzz
- `src/BandBuzzLite/Band/XiaomiSession.cs` — shared link session
  (auth, chunking, ack matching)
- `src/BandBuzzLite/Band/WinRt*.cs` — raw WinRT transport (default)
- `src/BandBuzzLite/Band/Win32*.cs` — raw Win32 GATT transport (`--win32`)
- `src/BandBuzzLite/Config.cs`, `SelfTest.cs`, `Program.cs`, `appsettings.json`
- `NativeMethods.txt` — the CsWin32 shopping list (only these get generated)

## Licence

AGPL-3.0-or-later (same as BandBuzz) — full text in [LICENSE](LICENSE).
Third-party lineage (BandBuzz, Gadgetbridge, BandMoot): [NOTICE.md](NOTICE.md).
