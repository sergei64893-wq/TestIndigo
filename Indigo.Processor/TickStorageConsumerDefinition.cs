using MassTransit;

public class TickStorageConsumerDefinition : ConsumerDefinition<TickStorageConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, 
        IConsumerConfigurator<TickStorageConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Принудительно включаем батчинг на уровне конфигуратора
        consumerConfigurator.Options<BatchOptions>(o => o
            .SetMessageLimit(100)
            .SetTimeLimit(TimeSpan.FromSeconds(5))
            .SetConcurrencyLimit(1));
    }
}