using System.Threading.Channels;

namespace CamGuard.Api.Features.Detection;

/// <summary>
/// Clips por analizar. No necesita disco: el estado persistente es Status = Analyzing en la BD, y
/// <see cref="DetectionService"/> reencola esas grabaciones al arrancar.
/// </summary>
public class DetectionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid recordingId) => _channel.Writer.TryWrite(recordingId);
}
