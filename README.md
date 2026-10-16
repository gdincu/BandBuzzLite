# BandBuzzLite

A **zero-dependency, ultra-compact rebuild of the BandBuzz utility for beaming Windows notifications to Xiaomi & Amazfit bands** — toasts → band cards, no phone needed.

Forwards Chrome/Teams/Outlook… to **Mi Band 7 / 7 Pro / 8 family + Amazfit family** over BLE — **no Windows pairing** (connects directly from advertisements).

* **~620 KB** published (`BandBuzzLite.exe` + `BandBuzzLite.dll`, pdb not required, sub-800 KB). Needs .NET 9 runtime, no heavy projection DLL.
* Dual-stack: **Xiaomi FE95** (Smart Band 7 Pro, 8 Active, 8/8 Pro/9/9 Pro, Redmi bands/watches) + **Huami/Zepp OS** (Mi Band 7 plain, Amazfit Band 7/Bip 5/GTR/GTS/T-Rex/Falcon/Cheetah/Balance/Active…).
* Same card protocol as [BandBuzz](../BandBuzz).

## Supported bands

Pick the family that matches your band in `appsettings.json` (`DeviceFamily`) or with `--family`. Both use the same `Mac` + `AuthKey` (32 hex).

| Family | How to select | Devices |
|---|---|---|
| **xiaomi** *(default)* | `DeviceFamily: "xiaomi"` or `--xiaomi` / `--family xiaomi` | Xiaomi Smart Band **8 Active** (primary, tested), **7 Pro**, **8**, **8 Pro**, **9**, **9 Pro**, Redmi Smart Band **2**, Redmi Smart Band **Pro**, Redmi Watch **3 / 3 Active / 4 / 5 Active / 5 Lite**, and any other Xiaomi band that advertises service `0000fe95` (FE95, chars `00000051`/`00000052`). |
| **huami** | `DeviceFamily: "huami"` or `--huami` / `--family huami` | **Xiaomi Smart Band 7** (plain, not Pro), Amazfit **Band 7**, **Bip 5 / Bip 5 Unity**, **GTR 3 / 3 Pro / 4 / GTR Mini**, **GTS 3 / 4 / 4 Mini**, **T-Rex 2 / 3 / Ultra**, **Falcon**, **Cheetah R / S / Pro**, **Balance**, **Active / Active Edge** — and any Zepp OS device with services `0000fee1` + `00001530` and endpoint `0x001e` (chunked `00000016`/`00000017`). |

> Not sure? Run `BandBuzzLite --diag` — it prints the family and hints: `FE95 service not found → try --huami`, `FEE1/3512 not found → try --xiaomi`.

## Quick start

### 1. Configure your band

Edit `appsettings.json` next to the exe:

```json
{
  "Mac": "AA:BB:CC:DD:EE:FF",
  "AuthKey": "00000000000000000000000000000000",
  "Filters": ["Chrome", "Teams"],
  "DeviceFamily": "xiaomi"
}
```

* `Mac` — find with `BandBuzzLite --scan` or in the Mi Fitness / Zepp app.
* `AuthKey` — 32 hex chars from pairing (same as BandBuzz, works for both families).
* `Filters` — which apps to forward (partial match, `Chrome` matches `Google Chrome`).
* `DeviceFamily` — `xiaomi` or `huami` as above.

You can also override on the command line:

```powershell
BandBuzzLite --mac AA:BB:CC:DD:EE:FF --key <32-hex> --family huami --filter Chrome,Teams
```

### 2. Send a test card (no install needed)

```powershell
dotnet publish src\BandBuzzLite\BandBuzzLite.csproj -c Release -o publish
.\publish\BandBuzzLite.exe --test "Hello" "It works"
# with a family override:
.\publish\BandBuzzLite.exe --family huami --test "Hello" "It works"
```

Check the band — the card stays until you dismiss it. The log shows each step, so you see exactly where it fails.

### 3. Forward live notifications (one-time install)

Watch mode reads Windows toasts, which needs a one-time identity:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
.\publish\BandBuzzLite.exe
```

`install.ps1` is safe to re-run. Press `Ctrl+C` to stop. Everything else (`--test`, `--scan`, `--diag`, `--selftest`) works without it.

## Commands

| Command | What it does |
|---|---|
| `BandBuzzLite` | Watch and forward (default) |
| `BandBuzzLite --test "Title" "Body" [app]` | One-off card, no toast access needed |
| `BandBuzzLite --list` | Show current Windows toasts |
| `BandBuzzLite --scan` | List known Bluetooth devices (find the MAC) |
| `BandBuzzLite --diag` | Troubleshooter: OS, package, Bluetooth, config + family |
| `BandBuzzLite --selftest` | Crypto/framing checks, no band needed |
| `BandBuzzLite --winrtcheck` | Check the Bluetooth stack |

Run `BandBuzzLite --help` for all options. Add `--win32` for the legacy transport (paired devices only).

## Settings

All settings live in `appsettings.json`:

* `Mac`, `AuthKey` — band identity
* `Filters` — app names to forward
* `DedupSeconds` — ignore duplicates within this window (`10`)
* `ReconnectSeconds` — retry delay if out of range (`5`)
* `MaxWrite` — BLE chunk size (`244`; Huami MTU = `MaxWrite+3`)
* `DeviceFamily` — `xiaomi` or `huami`

Missing or placeholder values fail fast — nothing is baked into the binary.

## Troubleshooting

* **`--diag` first** — one view for OS, package identity, toast access, Bluetooth radio, and your config.
* **No toasts?** Install the identity (`install.ps1`) and allow Windows notification access: *Settings → Privacy & security → Notifications → allow apps to access notifications*, then `--diag` should say `Allowed`.
* **Band not found?** `--scan` to confirm the MAC, ensure the band is advertising (screen on, nearby). Default transport needs no pairing; `--win32` does.
* **Wrong family?** If you see `FE95 service not found` try `--family huami`; if `FEE1/3512 not found` try `--family xiaomi`.
* **Still stuck?** `--test` logs every connection step.

## How it works & layout

* `Band/Xiaomi*.cs` — Xiaomi FE95 (`xiaomi.proto` type 7)
* `Band/Huami*.cs`, `EcdhB163.cs`, `ZeppOs*.cs` — Huami 2021 (chunked `00000016`/`00000017`, endpoint `0x001e`, ECDH B-163)
* `Band/WinRt*.cs` — default transport (raw WinRT, no projection DLL)
* `Band/Win32*.cs` — legacy transport (`--win32`)
* `Config.cs`, `SelfTest.cs`, `Program.cs`, `appsettings.json`, `NativeMethods.txt`, `install.ps1`

`--selftest` covers both families (AES-CCM/ECB, chunked roundtrip, ECDH). Details: Xiaomi → Gadgetbridge `devices/xiaomi/*Coordinator` + `XiaomiUuids`; Huami → `devices/huami/miband7/MiBand7Coordinator` → `ZeppOsSupport` + `Huami2021ChunkedEncoder`.

## Licence

AGPL-3.0-or-later (same as BandBuzz) — see [LICENSE](LICENSE). Third-party lineage: [NOTICE.md](NOTICE.md).
