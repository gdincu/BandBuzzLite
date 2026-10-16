# BandBuzzLite

Lightweight notifier for **Xiaomi FE95 bands** (Smart Band 7 Pro, 8 Active, 8/8 Pro, 9/9 Pro, Redmi Smart Band 2/Pro, Redmi Watch 3/4/5… any FE95 Xiaomi band) — forwards Windows notifications (e.g. Chrome, Teams) to your band as cards. Primary tested target is **Xiaomi Smart Band 8 Active**; **Xiaomi Smart Band 7 Pro** (Gadgetbridge `miband7pro/MiBand7ProCoordinator`) shares the same FE95 service and is now explicitly listed. Plain “Xiaomi Smart Band 7” (no Pro, Huami/Zepp OS – Gadgetbridge `huami/miband7/MiBand7Coordinator`) uses a different BLE stack and is **not** supported by this transport.

Same card protocol as [BandBuzz](../BandBuzz) (`XiaomiProto` / `XiaomiCrypto` / `XiaomiFrame` are verbatim ports), but packaged as a sub-800 KB single-purpose tool with no extra runtime baggage. It connects directly from Bluetooth LE advertisements, so Windows doesn't need to "see" the band first.

> **Gadgetbridge mapping:** FE95 bands → `devices/xiaomi/*Coordinator` + `service/devices/xiaomi/XiaomiUuids` + `XiaomiSupport`/`XiaomiNotificationService` (proto type 7, service `0000fe95-…`, chars `00000051-…`/`00000052-…`). Huami/Zepp OS Band 7 → `devices/huami/miband7` + `ZeppOsSupport` (endpoint `0x001e`) – not covered here.

`dotnet publish -c Release -o publish` → ~600 KB total (`BandBuzzLite.exe` + `BandBuzzLite.dll`). Requires the .NET 9 runtime.

## Quick start

### 1. Configure your band

Edit `appsettings.json` next to the exe and set your band's real values:

```json
{
  "Mac": "AA:BB:CC:DD:EE:FF",
  "AuthKey": "00000000000000000000000000000000",
  "Filters": [ "Chrome", "Teams" ]
}
```

* `Mac` — find it with `BandBuzzLite --scan` or in the Mi Fitness app.
* `AuthKey` — 32 hex characters from the band pairing (same as BandBuzz).
* `Filters` — which apps to forward when in watch mode.

You can also override on the command line: `--mac AA:BB:CC:DD:EE:FF --key <32-hex> --filter Chrome,Teams`

> No Bluetooth pairing needed. The default transport scans for the band's advertisements directly, just like the full BandBuzz app.

### 2. Send a test card (no install needed)

This works right after publish, without any setup:

```powershell
dotnet publish src\BandBuzzLite\BandBuzzLite.csproj -c Release -o publish
.\publish\BandBuzzLite.exe --test "Hello" "It works"
```

Check the band screen — the card stays until overwritten or dismissed. If anything goes wrong, the log tells you exactly which step failed.

### 3. Forward live notifications (one-time install)

Watch mode reads Windows toast notifications, which requires a one-time identity registration:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
.\publish\BandBuzzLite.exe            # now forwarding Chrome/Teams -> band
```

`install.ps1` is safe to re-run and handles publishing, packaging, and registration for you. Everything else (`--test`, `--scan`, `--diag`, etc.) works without it.

To stop watching, press `Ctrl+C`.

## Commands

| Command | What it does |
|---|---|
| `BandBuzzLite` | Watch and forward notifications (default) |
| `BandBuzzLite --list` | Show what Windows is displaying right now |
| `BandBuzzLite --test "Title" "Body" [app]` | Send a one-off test card |
| `BandBuzzLite --scan` | List known Bluetooth devices (to find the MAC) |
| `BandBuzzLite --diag` | Check setup and permissions |
| `BandBuzzLite --selftest` | Hardware-free crypto/framing checks |
| `BandBuzzLite --winrtcheck` | Check the Bluetooth stack (no band needed) |

Run `BandBuzzLite --help` for all options. Add `--win32` to use the legacy transport that only sees paired/remembered devices.

## Settings

All settings live in `appsettings.json` next to the exe:

* `Mac`, `AuthKey` — band identity
* `Filters` — app names to forward (partial match, e.g. `Chrome` matches `Google Chrome`)
* `DedupSeconds` — ignore duplicate toasts within this window (default `10`)
* `ReconnectSeconds` — retry delay if the band is out of range (default `5`)
* `MaxWrite` — BLE chunk size (default `244`)

Missing or placeholder values fail fast with a clear message — nothing is baked into the binary.

## Troubleshooting

* **`--diag` is your friend** — it reports OS version, package identity, toast access, Bluetooth radio, and your current config in one view.
* **No toasts in `--list` or watch mode?** Make sure the identity package is installed (`install.ps1`) and that Windows allows notification access: *Settings → Privacy & security → Notifications → allow apps to access notifications*, then re-run `--diag` until it says `Allowed`.
* **Band not found?** Run `--scan` to confirm the MAC, double-check `appsettings.json`, and make sure the band is advertising (screen on, nearby). The default transport needs no pairing; only `--win32` requires the band to be paired in Windows Settings.
* **Still stuck?** Run `--test` — it logs each connection step so you can see where it stops.

## Supported bands

* ✅ **Xiaomi FE95 family – supported (same BLE stack, no code fork):** Smart Band 8 Active (tested), Smart Band 7 Pro (`MiBand7ProCoordinator`), Smart Band 8 / 8 Pro, Smart Band 9 / 9 Pro, Redmi Smart Band 2 / Pro, Redmi Watch 3/4/5 series – any band whose Gadgetbridge coordinator extends `XiaomiCoordinator` and whose UUID set is `0000fe95-…` (`XiaomiUuids:5`).
* ❌ **Zepp OS / Huami – not supported:** plain Xiaomi Smart Band 7 (no “Pro”, `HuamiConst.XIAOMI_SMART_BAND7_NAME` → `huami/miband7/MiBand7Coordinator` → `ZeppOsCoordinator` / `ZeppOsNotificationService` endpoint `0x001e`). It needs a different BLE stack (file transfer, opaque pairing) – would require a separate implementation.

If your band advertises service `0000fe95-…` and accepts the phone-nonce → watch-nonce → `miwear-auth` HKDF handshake, it is supported; just set its MAC/key.

## How it works

* `Band/Xiaomi*.cs` — notification protocol (ported from BandBuzz / Gadgetbridge `xiaomi.proto` type 7)
* `Band/WinRt*.cs` — default Bluetooth transport using raw WinRT interop (no heavy projection DLL), connecting straight from advertisements
* `Band/Win32*.cs` — legacy Win32 GATT transport (`--win32` flag)

`--winrtcheck` and `--selftest` let you verify the stack without a band.

## Layout

* `src/BandBuzzLite/Band/Xiaomi*.cs` — protocol (`XiaomiProtocol` lists supported Gadgetbridge refs)
* `src/BandBuzzLite/Band/XiaomiSession.cs` — auth, chunking, ack matching
* `src/BandBuzzLite/Band/WinRt*.cs` — raw WinRT transport (default)
* `src/BandBuzzLite/Band/Win32*.cs` — raw Win32 GATT transport (`--win32`)
* `src/BandBuzzLite/Config.cs`, `SelfTest.cs`, `Program.cs`, `appsettings.json`
* `NativeMethods.txt` — CsWin32 API list
* `install.ps1` — one-command identity package setup

## Licence

AGPL-3.0-or-later (same as BandBuzz) — full text in [LICENSE](LICENSE).
Third-party lineage (BandBuzz, Gadgetbridge, BandMoot): [NOTICE.md](NOTICE.md).
