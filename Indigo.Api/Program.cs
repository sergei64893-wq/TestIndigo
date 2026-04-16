using Indigo.Application.Interfaces;
using Indigo.Application.Services;
using Indigo.Application.WebSocket;
using Indigo.Domain.ValueObjects;
using Indigo.Infrastructure.Database;
using Indigo.Infrastructure.Interfaces;
using Indigo.Infrastructure.Repositories;
using MassTransit;
using MassTransit.KafkaIntegration.Serializers;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Threading.Channels;
using Indigo.Middleware;
using StackExchange.Redis;

WebApplication app = null!;

try
{
    Log.Information("Starting Indigo Stock Data Aggregation System...");

    var builder = WebApplication.CreateBuilder(args);

    // Конфигурация Serilog логирования
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/indigo-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    // Добавляем Serilog как главный логгер
    builder.Host.UseSerilog();

    builder.Services.AddSingleton<IDuplicateDetector, RedisDuplicateDetector>();

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddScoped<ITickRepository, TickRepository>();

    var useMockClients = builder.Configuration.GetValue("UseMockClients", true);

    if (useMockClients)
    {
        // Используем mock клиентов для разработки (не требует интернета)
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            new MockWebSocketClient(sp.GetRequiredService<ILogger<MockWebSocketClient>>(), "BinanceMock-BTC"));
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            new MockWebSocketClient(sp.GetRequiredService<ILogger<MockWebSocketClient>>(), "BinanceMock-ETH"));
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            new MockWebSocketClient(sp.GetRequiredService<ILogger<MockWebSocketClient>>(), "KrakenMock"));

        Log.Information("Using MOCK WebSocket clients for development/testing");
    }
    else
    {
        // Используем реальные WebSocket клиентов для продакшна
        builder.Services.AddHttpClient<BinanceWebSocketClient>();
        builder.Services.AddHttpClient<KrakenWebSocketClient>();

        builder.Services.AddSingleton<IExchangeClient>(sp =>
            ActivatorUtilities.CreateInstance<BinanceWebSocketClient>(sp, "btcusdt"));
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            ActivatorUtilities.CreateInstance<BinanceWebSocketClient>(sp, "ethusdt"));
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            ActivatorUtilities.CreateInstance<BinanceWebSocketClient>(sp, "ethrub"));
        builder.Services.AddSingleton<IExchangeClient>(sp =>
            ActivatorUtilities.CreateInstance<KrakenWebSocketClient>(sp));

        Log.Information("Using REAL WebSocket clients for production");
    }
    
    builder.Services.AddSingleton(Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(10000)
    {
        SingleWriter = false,
        SingleReader = true
    }));
    
    builder.Services.AddTransient<ITickProcessor, TickProcessor>();
    builder.Services.AddTransient<ITickProducer, TickProducer>();

    builder.Services.AddHostedService<TickAggregationService>();
    
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    
    builder.Services.AddMassTransit(x =>
    {
        // Конфигурируем базовую шину с in-memory транспортом
        x.UsingInMemory((context, cfg) => { cfg.ConfigureEndpoints(context); });

        // Добавляем Kafka Rider
        x.AddRider(rider =>
        {
            rider.AddProducer<string, NormalizedTick>(
                builder.Configuration["Kafka:Topic"] ?? "exchange-ticks",
                (_, cfg) => { cfg.SetValueSerializer(new MassTransitJsonSerializer<NormalizedTick>()); });
            
            rider.UsingKafka((_, k) =>
            {
                k.Host(builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:29092");
            });
        });
    });
    
    builder.Services.AddMemoryCache();
    builder.Services.AddStackExchangeRedisCache(options =>
        options.Configuration = builder.Configuration["Redis:ConnectionString"]);
    
    // Регистрируем IConnectionMultiplexer для прямого доступа к Redis
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));
    
    app = builder.Build();
    
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Log.Information("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        Log.Information("Database migrations completed");
        
        // Инициализация и проверка Redis
        try
        {
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var db = redis.GetDatabase();
            
            // Проверяем подключение к Redis простым PING
            var pong = await db.PingAsync();
            Log.Information($"Redis connection verified successfully (ping: {pong.TotalMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Redis connection failed - duplicate detection may not work properly");
        }
    }
    
    var busControl = app.Services.GetRequiredService<IBusControl>();
    await busControl.StartAsync();
    Log.Information("MassTransit bus started");
    
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseRouting();
    
    if (!app.Environment.IsProduction())
    {
        app.UseHttpsRedirection();
    }
    
    app.UseGlobalExceptionHandler();

    app.MapControllers();
    
    app.MapGet("/health", async () =>
    {
        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow
        });
    }).WithName("Health");

    Log.Information("Starting web application...");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("Shutting down Indigo Stock Data Aggregation System");
    
    try
    {
        var busControl = app.Services.GetRequiredService<IBusControl>();
        await busControl.StopAsync();
        Log.Information("MassTransit bus stopped");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error stopping MassTransit bus");
    }

    await Log.CloseAndFlushAsync();
}