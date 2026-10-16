# BandBuzzLite

Lightweight notifier for **Xiaomi FE95 + Huami/Zepp OS** — forwards Windows notifications (e.g. Chrome, Teams) to your band as cards. Dual-stack: **Xiaomi FE95** (Smart Band 7 Pro, 8 Active, 8/8 Pro, 9/9 Pro, Redmi Smart Band 2/Pro, Redmi Watch 3/4/5… any `0000fe95` band) and **Huami/Zepp OS** (plain **Mi Band 7** `huami/miband7/MiBand7Coordinator` + Amazfit Band 7/Bip 5/GTR/GTS/T-Rex/Falcon/Cheetah/Balance/Active … any FEE1/3512 + endpoint `0x001e` device). Primary tested target is **Xiaomi Smart Band 8 Active**; both families share the same `--test`/`--watch` CLI – select with `DeviceFamily` (`xiaomi` default, `huami`).

Same card protocol as [BandBuzz](../BandBuzz) (Xiaomi `XiaomiProto`/`XiaomiCrypto`/`XiaomiFrame` are verbatim ports; Huami `ZeppOsProto`/`HuamiFrame`/`EcdhB163` ports `ZeppOsNotificationService` + `Huami2021ChunkedEncoder`), but packaged as a sub-800 KB single-purpose tool with no extra runtime baggage. It connects directly from Bluetooth LE advertisements, so Windows doesn't need to "see" the band first.

> **Gadgetbridge mapping:** FE95 → `devices/xiaomi/*Coordinator` + `service/devices/xiaomi/XiaomiUuids` + `XiaomiSupport` (proto type 7, `0000fe95`/`00000051`/`00000052`). Zepp OS → `devices/huami/miband7/MiBand7Coordinator` → `ZeppOsCoordinator`/`ZeppOsSupport` + `Huami2021ChunkedEncoder` (FEE1/`0000fee1` + `00001530`/`00000016`/`00000017`) + `ZeppOsNotificationService` endpoint `0x001e` + `InitOperation2021` (ECDH B-163, AES-ECB).

`dotnet publish -c Release -o publish` → ~620 KB total (`BandBuzzLite.exe` 144 KB + `BandBuzzLite.dll` 476 KB, total + deps ~630 KB, sub-800 KB). Requires the .NET 9 runtime.

## Quick start

### 1. Configure your band

Edit `appsettings.json` next to the exe and set your band's real values:

```json
{
  "Mac": "AA:BB:CC:DD:EE:FF",
  "AuthKey": "00000000000000000000000000000000",
  "Filters": [ "Chrome", "Teams" ],
  "DeviceFamily": "xiaomi"
}
```

* `Mac` — find it with `BandBuzzLite --scan` or in the Mi Fitness app.
* `AuthKey` — 32 hex characters from the band pairing (same as BandBuzz, same for both families).
* `Filters` — which apps to forward when in watch mode.
* `DeviceFamily` — `xiaomi` (FE95, default: 7 Pro/8 Active/8/9...) or `huami` (Zepp OS: plain Band 7 + Amazfit family). See `--help` / `--diag`.

You can also override on the command line: `--mac AA:BB:CC:DD:EE:FF --key <32-hex> --filter Chrome,Teams --family huami` (`--huami` / `--xiaomi` shorthands)

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

Run `BandBuzzLite --help` for all options. Add `--win32` to use the legacy transport that only sees paired/remembered devices; add `--huami` for plain Band 7 / Amazfit.

## Settings

All settings live in `appsettings.json` next to the exe:

* `Mac`, `AuthKey` — band identity
* `Filters` — app names to forward (partial match, e.g. `Chrome` matches `Google Chrome`)
* `DedupSeconds` — ignore duplicate toasts within this window (default `10`)
* `ReconnectSeconds` — retry delay if the band is out of range (default `5`)
* `MaxWrite` — BLE chunk size (default `244`, Huami MTU = MaxWrite+3 = `247`)
* `DeviceFamily` — `xiaomi` (FE95) or `huami` (Zepp OS, Mi Band 7 plain + extended family)

Missing or placeholder values fail fast with a clear message — nothing is baked into the binary.

## Troubleshooting

* **`--diag` is your friend** — it reports OS version, package identity, toast access, Bluetooth radio, and your current config in one view.
* **No toasts in `--list` or watch mode?** Make sure the identity package is installed (`install.ps1`) and that Windows allows notification access: *Settings → Privacy & security → Notifications → allow apps to access notifications*, then re-run `--diag` until it says `Allowed`.
* **Band not found?** Run `--scan` to confirm the MAC, double-check `appsettings.json`, and make sure the band is advertising (screen on, nearby). The default transport needs no pairing; only `--win32` requires the band to be paired in Windows Settings.
* **Still stuck?** Run `--test` — it logs each connection step so you can see where it stops.

## Supported bands

* ✅ **Xiaomi FE95 family – supported (FE95, `XiaomiSession`):** Smart Band 8 Active (tested), Smart Band 7 Pro (`MiBand7ProCoordinator`), Smart Band 8 / 8 Pro, Smart Band 9 / 9 Pro, Redmi Smart Band 2 / Pro, Redmi Watch 3/4/5 series – any band whose Gadgetbridge coordinator extends `XiaomiCoordinator` and whose UUID set is `0000fe95-…` (`XiaomiUuids:5`). Select `--family xiaomi` (default).
* ✅ **Huami/Zepp OS family – supported (FEE1/3512, `ZeppOsSession`):** plain **Xiaomi Smart Band 7** (`HuamiConst.XIAOMI_SMART_BAND7_NAME` → `huami/miband7/MiBand7Coordinator` → `ZeppOsCoordinator`, sources 260/262…), **Amazfit Band 7**, Bip 5/Unity, GTR 3/4, GTS 3/4/Mini, T-Rex 2/3/Ultra, Falcon, Cheetah R/S/Pro, Balance/Active/Active Edge … any Zepp OS device with `FEE1`/`00001530` + chunked-2021 (`00000016`/`00000017`) + endpoint `0x001e`. Select `--family huami` (`--huami`). Gadgetbridge: `ZeppOsSupport` + `Huami2021ChunkedEncoder` + `ZeppOsNotificationService` + `InitOperation2021` (ECDH B-163).

If your band advertises service `0000fe95-…` use `xiaomi`; if it exposes `0000fee1`/`00001530` + characteristics `00000016`/`00000017` use `huami`. The error “FE95 service not found” → try `--huami`; “FEE1/3512 not found” → try `--xiaomi`. Both share the same `AuthKey` (32 hex) and same `--test`/`--watch` CLI.

## How it works

* `Band/Xiaomi*.cs` — Xiaomi FE95 protocol (ported from BandBuzz / Gadgetbridge `xiaomi.proto` type 7, `XiaomiSupport`/`XiaomiCrypto`)
* `Band/Huami*.cs` + `Band/EcdhB163.cs` + `Band/ZeppOs*.cs` — Huami/Zepp OS 2021 protocol (ported from Gadgetbridge `Huami2021ChunkedEncoder`/`Huami2021ChunkedDecoder` + `ZeppOsNotificationService` endpoint `0x001e` + `InitOperation2021` ECDH B-163/AES-ECB)
* `Band/WinRt*.cs` — default Bluetooth transport using raw WinRT interop (no heavy projection DLL), connecting straight from advertisements (auto-selects FE95 vs FEE1 per `DeviceFamily`)
* `Band/Win32*.cs` — legacy Win32 GATT transport (`--win32` flag)

`--winrtcheck` and `--selftest` let you verify the stack without a band. `--selftest` now covers both families (FE95 + Huami: AES-ECB, chunked roundtrip, ECDH shared).

## Layout

* `src/BandBuzzLite/Band/Xiaomi*.cs` — Xiaomi FE95 protocol (`XiaomiProtocol` lists Gadgetbridge refs)
* `src/BandBuzzLite/Band/Huami*.cs`, `EcdhB163.cs`, `ZeppOs*.cs` — Huami/Zepp OS 2021 stack (ZeppOsCoordinator, plain Band 7)
* `src/BandBuzzLite/Band/XiaomiSession.cs` + `ZeppOsSession.cs` (`IBandSession`) — auth, chunking, ack matching
* `src/BandBuzzLite/Band/WinRt*.cs` — raw WinRT transports (WinRtBandClient FE95, WinRtHuamiClient Zepp OS)
* `src/BandBuzzLite/Band/Win32*.cs` — raw Win32 GATT transports (Win32BandClient FE95, Win32HuamiClient Zepp OS)
* `src/BandBuzzLite/Config.cs` (`DeviceFamily`), `SelfTest.cs`, `Program.cs`, `appsettings.json`
* `NativeMethods.txt` — CsWin32 API list
* `install.ps1` — one-command identity package setup

## Licence

AGPL-3.0-or-later (same as BandBuzz) — full text in [LICENSE](LICENSE).
Third-party lineage (BandBuzz, Gadgetbridge, BandMoot): [NOTICE.md](NOTICE.md).
