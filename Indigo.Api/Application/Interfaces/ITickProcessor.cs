namespace Indigo.Application.Interfaces;

/// <summary>
/// Интерфейс для обработки тиков от клиентов
/// </summary>
public interface ITickProcessor
{
    Task ProcessTicksAsync(CancellationToken cancellationToken);
}
