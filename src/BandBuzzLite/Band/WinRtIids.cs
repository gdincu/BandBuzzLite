// WinRT interface IIDs. Static ones are verified against Windows.winmd (see
// scratch/notes); the 8 generic-instantiation IIDs were computed with the
// winrt/base.h pinterface algorithm and cross-checked 6/6 against .NET's own
// WinRT interop oracle (PowerShell 5.1 MakeGenericType().GUID). The remaining
// 2 (vector instantiations) use the identical validated code path.
// Every computed IID is re-asserted at runtime via GetIids membership
// (WinRt.AssertIid), so a wrong IID fails fast with a clear message.

namespace BandBuzzLite.Band;

internal static class WinRtIids
{
    public const string IInspectable = "AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90";
    public const string IAsyncInfo = "00000036-0000-0000-C000-000000000046";
    public const string IClosable = "30D5A829-7FA4-4026-83BB-D75BAE4EA99E";
    public const string Watcher = "A6AC336F-F3D3-4297-8D6C-C81EA6623F40";
    public const string ReceivedArgs = "27987DDF-E596-41BE-8D43-9E6731D4A913";
    public const string DeviceStatics = "C8CF1A19-F0B6-4BF0-8689-41303DE2D9F4";
    public const string Device3 = "AEE9E493-44AC-40DC-AF33-B2C13C01CA46";
    public const string ServicesResult = "171DD3EE-016D-419D-838A-576CF475A3D8";
    public const string Service1 = "AC7B7C05-B33C-47CF-990F-6B8F5577DF71";
    public const string Service3 = "B293A950-0C53-437C-A9B3-5C3210C6E569";
    public const string CharacteristicsResult = "1194945C-B257-4F3E-9DB7-F68BC9A9AEF2";
    public const string Characteristic1 = "59CB50C1-5934-4F68-A198-EB864FA44E6B";
    public const string Characteristic3 = "3F3C663E-93D4-406B-B817-DB81F8ED53B3";
    public const string WriteResult = "4991DDB1-CB2B-44F7-99FC-D29A2871DC9B";
    public const string ValueChangedArgs = "D21BDB54-06E3-4ED8-A263-ACFAC8BA7313";
    public const string Buffer = "905A0FE0-BC53-11DF-8C49-001E4FC686DA";
    public const string BufferFactory = "71AF914D-C10F-484B-BC50-14BC623B3A27";
    public const string BufferByteAccess = "905A0FEF-BC53-11DF-8C49-001E4FC686DA";

    // Computed generic instantiations (see above for provenance).
    public const string OpDevice = "375F9D67-74A2-5F91-A11D-169093718D41";    public const string OpServices = "E7C667F6-E874-500F-86FF-760CA6F07A58";
    public const string OpCharacteristics = "0972194A-AC1C-5536-9886-27E58A18F273";
    public const string OpWrite = "E83B4534-BD14-5A9B-A53B-17CC02A2A8A8";
    public const string RecvHandler = "90EB4ECA-D465-5EA0-A61C-033C8C5ECEF2";
    public const string ValueHandler = "C1F420F6-6292-5760-A2C9-9DDF98683CFC";
    public const string VecServices = "7C8E7FDD-A1A1-528A-81D1-296769227A08";
    public const string VecCharacteristics = "CB3AB3AE-B561-504F-A808-599DECEB2DF4";

    // Toast listener (static IIDs from winmd; OpAccess/TehListener oracle-checked,
    // vector IIDs same validated path, asserted at runtime).
    public const string ListenerStatics = "FF6123CF-4386-4AA3-B73D-B804E5B63B23";
    public const string Listener = "62553E41-8A06-4CEF-8215-6033A5BE4B03";
    public const string UserNotification = "ADF7E52F-4E53-42D5-9C33-EB5EA515B23E";
    public const string AppInfo = "CF7F59B3-6A09-4DE8-A6C0-5792D56880D1";
    public const string AppDisplayInfo = "1AEB1103-E4D4-41AA-A4F6-C4A276E79EAC";
    public const string Notification = "108037FE-EB76-4F82-97BC-DA07530A2E20";
    public const string NotificationVisual = "68835B8E-AA56-4E11-86D3-5F9A6957BC5B";
    public const string NotificationBinding = "F29E4B85-0370-4AD3-B4EA-DA9E35E7EABF";
    public const string AdaptiveNotificationText = "46D4A3BE-609A-4326-A40B-BFDE872034A3";
    public const string OpAccess = "0FBAD8C7-086F-5BF9-81E2-8D79E7184803";
    // Nested generics are NOT computed (nesting encoding unverified): this IID
    // came from the .NET oracle (method ReturnType.GUID) and the live OS set.
    public const string OpNotifVec = "BF7F3D3C-230F-54EA-AD74-0CF6C55CD8D1";
    public const string NotifChangedHandler = "10242902-B897-5507-9922-2C0A7D34464D";
    public const string VecUserNotification = "5A08F98C-8E45-5705-AF54-F5418E598392";
    public const string VecAdaptiveText = "B60DE92B-4E12-55AF-B42F-AFE2D70BA278";
}
