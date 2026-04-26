using MassTransit;

public class TickStorageConsumerDefinition : ConsumerDefinition<TickStorageConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, 
        IConsumerConfigurator<TickStorageConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Принудительно включаем батчинг на уровне конфигуратора
        consumerConfigurator.Options<BatchOptions>(o => o
            .SetMessageLimit(1000)
            .SetTimeLimit(TimeSpan.FromSeconds(1))
            .SetConcurrencyLimit(1));
    }
}