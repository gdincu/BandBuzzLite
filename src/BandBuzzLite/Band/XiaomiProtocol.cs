namespace BandBuzzLite.Band;

/// <summary>
/// Xiaomi BLE identity for FE95 encrypted bands (service FE95, chars 0x0051/0x0052).
/// Gadgetbridge groups these under XiaomiCoordinator / XiaomiSupport – they all
/// share the same service/char UUIDs and the same proto2 auth+notification flow:
///   • Xiaomi Smart Band 8 Active (tested primary target)
///   • Xiaomi Smart Band 7 Pro (same stack – HuamiConst.XIAOMI_SMART_BAND7_PRO_NAME)
///   • Xiaomi Smart Band 8 / 8 Pro / 9 / 9 Pro, Redmi Smart Band 2 / Pro,
///     Redmi Watch 3/4/5 series etc. (see XiaomiUuids.BLE_UUIDS["0000fe95-..."]).
/// Plain "Xiaomi Smart Band 7" (no Pro) is Zepp OS / Huami (HuamiConst.XIAOMI_SMART_BAND7_NAME,
/// ZeppOsSupport + endpoint 0x001e) – different BLE stack and NOT covered here.
/// </summary>
internal static class XiaomiProtocol
{
    public static readonly Guid ServiceFe95 = Guid.Parse("0000fe95-0000-1000-8000-00805f9b34fb");
    public static readonly Guid CharCommandRead = Guid.Parse("00000051-0000-1000-8000-00805f9b34fb");
    public static readonly Guid CharCommandWrite = Guid.Parse("00000052-0000-1000-8000-00805f9b34fb");

    // Gadgetbridge coordinators that map to this transport (all Xiaomi FE95):
    //   MiBand7ProCoordinator ("Xiaomi Smart Band 7 Pro.*"), MiBand8Coordinator
    //   ("^Xiaomi Smart Band 8 [A-Z0-9]{4}$"), MiBand8ActiveCoordinator
    //   ("^Xiaomi( Smart)? Band 8 Active [A-Z0-9]{4}$"), MiBand8ProCoordinator,
    //   MiBand9Coordinator, Redmi* coordinators – see
    //   app/src/main/java/.../devices/xiaomi/*Coordinator.java and
    //   service/devices/xiaomi/XiaomiUuids.java.
    public static readonly string[] SupportedDeviceExamples =
    [
        "Xiaomi Smart Band 7 Pro",
        "Xiaomi Smart Band 8 Active",
        "Xiaomi Smart Band 8",
        "Xiaomi Smart Band 8 Pro",
        "Xiaomi Smart Band 9 / 9 Pro",
        "Redmi Smart Band 2 / Pro, Redmi Watch 3/4/5 series, etc. (any FE95 Xiaomi band)",
    ];

    public static readonly string UnsupportedHuamiBand7Note =
        "Plain 'Xiaomi Smart Band 7' (no 'Pro') is Huami/Zepp OS (Gadgetbridge: huami/miband7/MiBand7Coordinator → ZeppOsCoordinator) – " +
        "it uses a different BLE stack (endpoints, file transfer, 0x001e notifications) and is not supported by this FE95 transport.";

    public const int AuthType = 1;
    public const int CmdNonce = 26;
    public const int CmdAuth = 27;

    public const int NotifType = 7;
    public const int CmdNotifSend = 0;
    public const int CmdScreenOnSet = 7;

    /// <summary>32 hex chars, optionally 0x-prefixed.</summary>
    public static bool IsValidAuthKey(string key)
    {
        var k = key.Trim();
        if (k.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            k = k[2..];
        return k.Length == 32 && k.All(c => Uri.IsHexDigit(c));
    }

    public static byte[] ParseAuthKey(string key)
    {
        var k = key.Trim();
        if (k.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            k = k[2..];
        if (k.Length != 32 || !k.All(Uri.IsHexDigit))
            throw new ArgumentException("Auth key must be 32 hex chars (or 0x+32).", nameof(key));
        return Convert.FromHexString(k);
    }

    /// <summary>AA:BB:CC:DD:EE:FF (case-insensitive, ':' or '-' separated) to ulong for WinRT.</summary>
    public static ulong ParseMac(string mac)
    {
        var hex = mac.Trim().Replace(":", "").Replace("-", "");
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit))
            throw new ArgumentException("Use AA:BB:CC:DD:EE:FF.", nameof(mac));
        return Convert.ToUInt64(hex, 16);
    }
}
