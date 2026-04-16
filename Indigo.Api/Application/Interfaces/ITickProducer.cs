namespace Indigo.Application.Interfaces;

/// <summary>
/// Интерфейс для производства тиков в Kafka
/// </summary>
public interface ITickProducer
{
    Task ProduceTicksAsync(CancellationToken cancellationToken);
}
