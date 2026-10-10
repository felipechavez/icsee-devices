using System.Threading.Channels;
using CamGuard.Api.Common.Data;

namespace CamGuard.Api.Common.Events;

/// <summary>Cambio de estado publicado por Monitoring y consumido por Capture.</summary>
public record CameraStateChanged(int CameraId, CameraEventType Type, DateTimeOffset OccurredAt);

/// <summary>Canal en memoria entre el slice de monitoreo y el de grabación.</summary>
public class CameraEventChannel
{
    private readonly Channel<CameraStateChanged> _channel =
        Channel.CreateUnbounded<CameraStateChanged>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<CameraStateChanged> Writer => _channel.Writer;
    public ChannelReader<CameraStateChanged> Reader => _channel.Reader;
}
