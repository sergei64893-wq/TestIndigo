using Confluent.Kafka;
using Indigo.Domain.ValueObjects;
using Indigo.Domain.Entities;
using Indigo.Infrastructure.Database;
using Indigo.Infrastructure.Interfaces;
using Indigo.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ObjectPool;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<ITickRepository, TickRepository>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<ObjectPool<List<PriceTick>>>(sp =>
    ObjectPool.Create(new DefaultPooledObjectPolicy<List<PriceTick>>()));

builder.Services.AddMassTransit(x =>
{
    x.AddRider(rider =>
    {
        // Регистрируем ПАРУ: консьюмер и его определение
        rider.AddConsumer<TickStorageConsumer, TickStorageConsumerDefinition>();

        rider.UsingKafka((context, k) =>
        {
            k.Host(builder.Configuration["Kafka:BootstrapServers"]);

            var topic = builder.Configuration["Kafka:Topic"];
            var groupId = builder.Configuration["Kafka:GroupId"];

            k.TopicEndpoint<string, NormalizedTick>(topic, groupId, c =>
            {
                c.AutoOffsetReset = AutoOffsetReset.Earliest;
                c.UseRawJsonSerializer();

                // Эти лимиты открывают "окно" для батча
                c.ConcurrentMessageLimit = 100;
                c.PrefetchCount = 500;
                c.ConcurrentDeliveryLimit = 100;
                // Definition подтянется автоматически
                c.ConfigureConsumer<TickStorageConsumer>(context);
            });
        });
    });


    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});


var host = builder.Build();
host.Run();