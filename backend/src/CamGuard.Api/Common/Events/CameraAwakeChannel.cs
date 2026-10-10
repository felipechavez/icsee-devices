using System.Threading.Channels;

namespace CamGuard.Api.Common.Events;

/// <summary>
/// Aviso de "la cámara despertó" de Monitoring a Devices. Va aparte de <see cref="CameraEventChannel"/>
/// porque ese tiene un solo lector (Capture).
/// </summary>
public class CameraAwakeChannel
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<int> Writer => _channel.Writer;
    public ChannelReader<int> Reader => _channel.Reader;
}
