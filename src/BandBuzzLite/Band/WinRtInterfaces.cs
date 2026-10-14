// Hand-written WinRT interface projections over ComWrappers source-gen.
// Method order == winmd metadata order == vtable order (verified per-type
// against Windows.winmd; see WinRtIids for IID provenance). Only called
// methods need exact signatures; uncalled slots use size-correct placeholders
// (long = 8B struct, int = enum, nint = object/HSTRING).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BandBuzzLite.Band;

[StructLayout(LayoutKind.Sequential)]
internal struct EventRegistrationToken
{
    public long Value;
}

internal enum GattStatus
{
    Success = 0,
    Unreachable = 1,
    ProtocolError = 2,
    AccessDenied = 3,
}

internal enum CccdValue
{
    None = 0,
    Notify = 1,
    Indicate = 2,
}

internal enum GattWriteOption
{
    WithResponse = 0,
    WithoutResponse = 1,
}

[GeneratedComInterface, Guid(WinRtIids.IInspectable)]
internal partial interface IWinRtInspectable
{
    void GetIids(out uint count, out nint array);
    void GetRuntimeClassName(out nint name);
    void GetTrustLevel(out int level);
}

[GeneratedComInterface, Guid(WinRtIids.IAsyncInfo)]
internal partial interface IWinRtAsyncInfo : IWinRtInspectable
{
    uint GetId();
    int GetStatus();
    int GetErrorCode();
}

[GeneratedComInterface, Guid(WinRtIids.IClosable)]
internal partial interface IWinRtClosable : IWinRtInspectable
{
    void Close();
}

// IAsyncOperation<T> instantiations: put_Completed, get_Completed, GetResults.
[GeneratedComInterface, Guid(WinRtIids.OpDevice)]
internal partial interface IOpDevice : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    nint GetResults();
}

[GeneratedComInterface, Guid(WinRtIids.OpServices)]
internal partial interface IOpServices : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    nint GetResults();
}

[GeneratedComInterface, Guid(WinRtIids.OpCharacteristics)]
internal partial interface IOpCharacteristics : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    nint GetResults();
}

[GeneratedComInterface, Guid(WinRtIids.OpWrite)]
internal partial interface IOpWrite : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    nint GetResults();
}

// (No IOpStatus: the only status-returning op (WriteCCCD) actually yields
// GattWriteResult per cppwinrt + live OS identity; v1 status ops are unused.)

[GeneratedComInterface, Guid(WinRtIids.Watcher)]
internal partial interface IAdvertWatcher : IWinRtInspectable
{
    long GetMinSamplingInterval();
    long GetMaxSamplingInterval();
    long GetMinOutOfRangeTimeout();
    long GetMaxOutOfRangeTimeout();
    int GetStatus();
    int GetScanningMode();
    void SetScanningMode(int value);
    nint GetSignalStrengthFilter();
    void SetSignalStrengthFilter(nint value);
    nint GetAdvertisementFilter();
    void SetAdvertisementFilter(nint value);
    void Start();
    void Stop();
    EventRegistrationToken AddReceived(nint handler);
    void RemoveReceived(EventRegistrationToken token);
}

[GeneratedComInterface, Guid(WinRtIids.ReceivedArgs)]
internal partial interface IReceivedArgs : IWinRtInspectable
{
    short GetRawSignalStrength();
    ulong GetBluetoothAddress();
}

[GeneratedComInterface, Guid(WinRtIids.DeviceStatics)]
internal partial interface IDeviceStatics : IWinRtInspectable
{
    nint FromIdAsync(nint deviceId);
    nint FromBluetoothAddressAsync(ulong address);
}

[GeneratedComInterface, Guid(WinRtIids.Device3)]
internal partial interface IDevice3 : IWinRtInspectable
{
    nint GetDeviceAccessInformation();
    nint RequestAccessAsync();
    nint GetGattServicesAsync();
    nint GetGattServicesAsyncWithCache(int cacheMode);
    nint GetGattServicesForUuidAsync(Guid uuid);
    nint GetGattServicesForUuidAsyncWithCache(Guid uuid, int cacheMode);
}

[GeneratedComInterface, Guid(WinRtIids.ServicesResult)]
internal partial interface IServicesResult : IWinRtInspectable
{
    int GetStatus();
    nint GetProtocolError();
    nint GetServices();
}

[GeneratedComInterface, Guid(WinRtIids.Service1)]
internal partial interface IService1 : IWinRtInspectable
{
    nint GetCharacteristics(Guid uuid);
    nint GetIncludedServices(Guid uuid);
    nint GetDeviceId();
    Guid GetUuid();
}

[GeneratedComInterface, Guid(WinRtIids.Service3)]
internal partial interface IService3 : IWinRtInspectable
{
    nint GetDeviceAccessInformation();
    nint GetSession();
    int GetSharingMode();
    nint RequestAccessAsync();
    nint OpenAsync(int sharingMode);
    nint GetCharacteristicsAsync();
}

[GeneratedComInterface, Guid(WinRtIids.CharacteristicsResult)]
internal partial interface ICharacteristicsResult : IWinRtInspectable
{
    int GetStatus();
    nint GetProtocolError();
    nint GetCharacteristics();
}

[GeneratedComInterface, Guid(WinRtIids.Characteristic1)]
internal partial interface ICharacteristic1 : IWinRtInspectable
{
    nint GetDescriptors(Guid uuid);
    int GetCharacteristicProperties();
    int GetProtectionLevel();
    void SetProtectionLevel(int value);
    nint GetUserDescription();
    Guid GetUuid();
    ushort GetAttributeHandle();
    nint GetPresentationFormats();
    nint ReadValueAsync();
    nint ReadValueAsyncWithCache(int cacheMode);
    nint WriteValueAsync(nint value);
    nint WriteValueAsyncWithOption(nint value, int writeOption);
    nint ReadCccdAsync();
    nint WriteCccdAsync(int value);
    EventRegistrationToken AddValueChanged(nint handler);
    void RemoveValueChanged(EventRegistrationToken token);
}

[GeneratedComInterface, Guid(WinRtIids.Characteristic3)]
internal partial interface ICharacteristic3 : IWinRtInspectable
{
    nint GetDescriptorsAsync();
    nint GetDescriptorsAsyncWithCache(int cacheMode);
    nint GetDescriptorsForUuidAsync(Guid uuid);
    nint GetDescriptorsForUuidAsyncWithCache(Guid uuid, int cacheMode);
    nint WriteValueWithResultAsync(nint value);
    nint WriteValueWithResultAsyncWithOption(nint value, int writeOption);
    nint WriteCccdWithResultAsync(int value);
}

[GeneratedComInterface, Guid(WinRtIids.WriteResult)]
internal partial interface IWriteResult : IWinRtInspectable
{
    int GetStatus();
}

[GeneratedComInterface, Guid(WinRtIids.ValueChangedArgs)]
internal partial interface IValueChangedArgs : IWinRtInspectable
{
    nint GetCharacteristicValue();
}

[GeneratedComInterface, Guid(WinRtIids.BufferFactory)]
internal partial interface IBufferFactory : IWinRtInspectable
{
    nint Create(uint capacity);
}

[GeneratedComInterface, Guid(WinRtIids.Buffer)]
internal partial interface IWinRtBuffer : IWinRtInspectable
{
    uint GetCapacity();
    uint GetLength();
    void SetLength(uint value);
}

// Classic COM (IUnknown-based, no IInspectable): IBufferByteAccess::Buffer.
[GeneratedComInterface, Guid(WinRtIids.BufferByteAccess)]
internal partial interface IBufferByteAccess
{
    void GetBuffer(out nint value);
}

// IVectorView<T> instantiations: GetAt, get_Size (trailing methods omitted).
[GeneratedComInterface, Guid(WinRtIids.VecServices)]
internal partial interface IVecServices : IWinRtInspectable
{
    void GetAt(uint index, out nint item);
    uint GetSize();
}

[GeneratedComInterface, Guid(WinRtIids.VecCharacteristics)]
internal partial interface IVecCharacteristics : IWinRtInspectable
{
    void GetAt(uint index, out nint item);
    uint GetSize();
}

// WinRT delegates (IUnknown-based): TypedEventHandler<TSender, TArgs>.
[GeneratedComInterface, Guid(WinRtIids.RecvHandler)]
internal partial interface IRecvHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface, Guid(WinRtIids.ValueHandler)]
internal partial interface IValueHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface, Guid(WinRtIids.NotifChangedHandler)]
internal partial interface INotifChangedHandler
{
    void Invoke(nint sender, nint args);
}

// ---- toast listener ----

[GeneratedComInterface, Guid(WinRtIids.ListenerStatics)]
internal partial interface IListenerStatics : IWinRtInspectable
{
    nint GetCurrent();
}

[GeneratedComInterface, Guid(WinRtIids.Listener)]
internal partial interface IToastListener : IWinRtInspectable
{
    nint RequestAccessAsync();
    int GetAccessStatus();
    EventRegistrationToken AddNotificationChanged(nint handler);
    void RemoveNotificationChanged(EventRegistrationToken token);
    nint GetNotificationsAsync(int kinds);
}

[GeneratedComInterface, Guid(WinRtIids.UserNotification)]
internal partial interface IUserNotification : IWinRtInspectable
{
    nint GetNotification();
    nint GetAppInfo();
    uint GetId();
}

[GeneratedComInterface, Guid(WinRtIids.AppInfo)]
internal partial interface IAppInfo : IWinRtInspectable
{
    nint GetId();
    nint GetAppUserModelId();
    nint GetDisplayInfo();
}

[GeneratedComInterface, Guid(WinRtIids.AppDisplayInfo)]
internal partial interface IAppDisplayInfo : IWinRtInspectable
{
    nint GetDisplayName();
}

[GeneratedComInterface, Guid(WinRtIids.Notification)]
internal partial interface IToastNotification : IWinRtInspectable
{
    nint GetExpirationTime();
    void SetExpirationTime(nint value);
    nint GetVisual();
}

[GeneratedComInterface, Guid(WinRtIids.NotificationVisual)]
internal partial interface IToastVisual : IWinRtInspectable
{
    nint GetLanguage();
    void SetLanguage(nint value);
    nint GetBindings();
    nint GetBinding(nint templateName);
}

[GeneratedComInterface, Guid(WinRtIids.NotificationBinding)]
internal partial interface IToastBinding : IWinRtInspectable
{
    nint GetTemplate();
    void SetTemplate(nint value);
    nint GetLanguage();
    void SetLanguage(nint value);
    nint GetHints();
    nint GetTextElements();
}

[GeneratedComInterface, Guid(WinRtIids.AdaptiveNotificationText)]
internal partial interface IAdaptiveText : IWinRtInspectable
{
    nint GetText();
}

[GeneratedComInterface, Guid(WinRtIids.OpAccess)]
internal partial interface IOpAccess : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    int GetResults();
}

[GeneratedComInterface, Guid(WinRtIids.OpNotifVec)]
internal partial interface IOpNotifVec : IWinRtInspectable
{
    void SetCompleted(nint handler);
    nint GetCompleted();
    nint GetResults();
}

[GeneratedComInterface, Guid(WinRtIids.VecUserNotification)]
internal partial interface IVecUserNotification : IWinRtInspectable
{
    void GetAt(uint index, out nint item);
    uint GetSize();
}

[GeneratedComInterface, Guid(WinRtIids.VecAdaptiveText)]
internal partial interface IVecAdaptiveText : IWinRtInspectable
{
    void GetAt(uint index, out nint item);
    uint GetSize();
}
