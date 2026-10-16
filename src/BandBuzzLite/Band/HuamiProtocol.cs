namespace BandBuzzLite.Band;

/// <summary>
/// Huami / Zepp OS BLE identity for the extended family sharing MiBand7 protocol.
/// Gadgetbridge: huami/miband7/MiBand7Coordinator → ZeppOsCoordinator → HuamiCoordinator → ZeppOsSupport.
/// Services: FEE0 (MiBand), FEE1 (MiBand2, scan filter), 3512 custom service.
/// Key characteristics: 00000009 AUTH, 00000016/00000017 chunked-2021, heart-rate, battery etc.
/// Notification endpoint 0x001e (ZeppOsNotificationService), auth endpoint 0x0082 (InitOperation2021).
/// Extended family (same chunked-2021 + 0x001e): Mi Band 7, Amazfit Band 7/Bip 5/GTR 3-4/GTS 3-4/T-Rex 2/3,
/// Falcon, Cheetah, Balance, Active etc. – see HuamiConst + ZeppOsCoordinator.getDeviceSources().
/// </summary>
internal static class HuamiProtocol
{
    // Services (Base UUID 0000xxxx-0000-1000-8000-00805f9b34fb except 3512)
    public static readonly Guid ServiceFee0 = Guid.Parse("0000fee0-0000-1000-8000-00805f9b34fb");
    public static readonly Guid ServiceFee1 = Guid.Parse("0000fee1-0000-1000-8000-00805f9b34fb");
    public static readonly Guid ServiceFtg = Guid.Parse("00001530-0000-3512-2118-0009af100700");

    // Characteristics inside 3512 service (little-endian UUIDs)
    public static readonly Guid CharAuth = Guid.Parse("00000009-0000-3512-2118-0009af100700");
    public static readonly Guid CharChunkedWrite2021 = Guid.Parse("00000016-0000-3512-2118-0009af100700");
    public static readonly Guid CharChunkedRead2021 = Guid.Parse("00000017-0000-3512-2118-0009af100700");
    public static readonly Guid CharChunkedLegacy = Guid.Parse("00000020-0000-3512-2118-0009af100700");
    public static readonly Guid CharFileTransfer = Guid.Parse("00000023-0000-3512-2118-0009af100700");

    // Zepp OS endpoints (Huami2021 + ZeppOs)
    public const short EndpointAuth = 0x0082; // InitOperation2021 CHUNKED2021_ENDPOINT_AUTH
    public const short EndpointNotification = 0x001e; // ZeppOsNotificationService.ENDPOINT
    public const short EndpointServices = 0x0000;
    public const short EndpointFileTransfer = 0x000d;

    // Notification commands (ZeppOsNotificationService)
    public const byte NotifCmdCapabilitiesRequest = 0x01;
    public const byte NotifCmdCapabilitiesResponse = 0x02;
    public const byte NotifCmdSend = 0x03;
    public const byte NotifCmdReply = 0x04;
    public const byte NotifCmdDismiss = 0x05;
    public const byte NotifCmdReplyAck = 0x06;
    public const byte NotifCmdIconRequest = 0x10;
    public const byte NotifCmdIconRequestAck = 0x11;
    public const byte NotifTypeNormal = 0xFA;
    public const byte NotifTypeCall = 0x03;
    public const byte NotifTypeSms = 0x05;
    public const byte NotifSubcmdShow = 0x00;
    public const byte NotifSubcmdDismissFromPhone = 0x02;

    public static readonly string[] SupportedHuamiExamples =
    [
        "Xiaomi Smart Band 7 (MiBand7Coordinator – Zepp OS)",
        "Amazfit Band 7 (HuamiConst.AMAZFIT_BAND7_NAME)",
        "Amazfit Bip 5 / Bip 5 Unity",
        "Amazfit GTR 3 / 3 Pro / 4, GTS 3 / 4 / 4 Mini, GTR Mini",
        "Amazfit T-Rex 2 / 3 / Ultra, Falcon, Cheetah R/S/Pro",
        "Amazfit Balance / Active / Active Edge",
        "Any Zepp Os device with chunked-2021 + endpoint 0x001e",
    ];

    // MiBand7Coordinator specifics
    public static readonly string MiBand7Name = "Xiaomi Smart Band 7"; // HuamiConst.XIAOMI_SMART_BAND7_NAME
    public static readonly int[] MiBand7Sources = [260, 262, 263, 264, 265];

    public static bool IsHuamiAuthKey(string key) => XiaomiProtocol.IsValidAuthKey(key);
    public static byte[] ParseHuamiAuthKey(string key) => XiaomiProtocol.ParseAuthKey(key);
    public static ulong ParseMac(string mac) => XiaomiProtocol.ParseMac(mac);
}
