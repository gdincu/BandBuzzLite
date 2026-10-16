using System.Text;

namespace BandBuzzLite.Band;

/// <summary>
/// Zepp OS notification payloads (endpoint 0x001e), port of Gadgetbridge ZeppOsNotificationService.sendNotification.
/// Gadgetbridge: service/devices/huami/zeppos/services/ZeppOsNotificationService.java:288-380
/// Wire: 0x03 id LE32 type 0xFA/0x05 subcmd 0x00 package 0x00 title 0x00 body 0x00 appName 0x00 hasReply (+ v5 silent, picture, key)
/// Call: 0x03 id 0 type 0x03 start/end 0x00 name 0x00 0x00 0x00 number 0x00 replyFlag
/// </summary>
internal static class ZeppOsProto
{
    public static byte[] BuildNotification(string title, string body, string package, string appName, int id, bool hasReply = false)
    {
        var t = TruncateUtf8(title, 120);
        var b = TruncateUtf8(body, 512);
        using var ms = new MemoryStream();
        ms.WriteByte(HuamiProtocol.NotifCmdSend);
        ms.Write(BitConverter.GetBytes(id), 0, 4);
        ms.WriteByte(HuamiProtocol.NotifTypeNormal);
        ms.WriteByte(HuamiProtocol.NotifSubcmdShow);
        WriteString(ms, package);
        WriteString(ms, t);
        WriteString(ms, b);
        WriteString(ms, appName);
        ms.WriteByte((byte)(hasReply ? 1 : 0));
        return ms.ToArray();
    }

    public static byte[] BuildCapabilitiesRequest() => [HuamiProtocol.NotifCmdCapabilitiesRequest];

    private static void WriteString(MemoryStream ms, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        ms.Write(b, 0, b.Length);
        ms.WriteByte(0);
    }
    private static void WriteString(MemoryStream ms, byte[] b)
    {
        ms.Write(b, 0, b.Length);
        ms.WriteByte(0);
    }
    private static byte[] TruncateUtf8(string s, int maxBytes)
    {
        var b = Encoding.UTF8.GetBytes(s);
        if (b.Length <= maxBytes) return b;
        var cut = maxBytes;
        while (cut > 0 && (b[cut] & 0xC0) == 0x80) cut--;
        return b[..cut];
    }
}
