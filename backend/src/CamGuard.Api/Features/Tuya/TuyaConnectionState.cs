namespace CamGuard.Api.Features.Tuya;

public record TuyaConnection(bool Connected, DateTimeOffset Since, string? LastError);

/// <summary>Estado de la conexión con el Message Service de Tuya, leído por Features/Health.</summary>
public class TuyaConnectionState(TimeProvider clock)
{
    private TuyaConnection _current = new(false, clock.GetUtcNow(), null);

    public TuyaConnection Current => Volatile.Read(ref _current);

    public void Connected() => Volatile.Write(ref _current, new TuyaConnection(true, clock.GetUtcNow(), null));

    /// <summary>Conserva el "desde" mientras siga caída: los reintentos no reinician la cuenta.</summary>
    public void Disconnected(string error)
    {
        var current = Current;
        Volatile.Write(ref _current, new TuyaConnection(false, current.Connected ? clock.GetUtcNow() : current.Since, error));
    }
}
