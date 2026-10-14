// CCW event sinks: managed delegates exposed as WinRT TypedEventHandler
// COM objects. Instances must stay rooted (fields) while subscribed; the
// native side holds a COM ref, the field holds the managed ref.

using System.Runtime.InteropServices.Marshalling;

namespace BandBuzzLite.Band;

[GeneratedComClass]
internal sealed partial class RecvHandler(Action<nint> onArgs) : IRecvHandler
{
    public void Invoke(nint sender, nint args) => onArgs(args);
}

[GeneratedComClass]
internal sealed partial class ValueHandler(Action<nint> onArgs) : IValueHandler
{
    public void Invoke(nint sender, nint args) => onArgs(args);
}

[GeneratedComClass]
internal sealed partial class NotifChangedHandler(Action onChanged) : INotifChangedHandler
{
    public void Invoke(nint sender, nint args) => onChanged();
}
