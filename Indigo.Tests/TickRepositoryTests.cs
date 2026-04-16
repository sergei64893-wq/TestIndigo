using Indigo.Domain.Entities;
using Indigo.Infrastructure.Database;
using Indigo.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Indigo.Tests;

public class TickRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly Mock<ILogger<TickRepository>> _loggerMock;
    private readonly TickRepository _repository;

    public TickRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _loggerMock = new Mock<ILogger<TickRepository>>();
        _repository = new TickRepository(_context, _loggerMock.Object);
    }

    [Fact]
    public async Task AddRangeAsync_ShouldAddTicksToDatabase()
    {
        // Arrange
        var ticks = new List<PriceTick>
        {
            new PriceTick { Id = Guid.NewGuid(), Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = "Binance", DuplicateCheckHash = "hash1", RawData = "{}" },
            new PriceTick { Id = Guid.NewGuid(), Ticker = "ETHUSDT", Price = 3000M, Volume = 2.0M, Timestamp = 1640995201, Source = "Binance", DuplicateCheckHash = "hash2", RawData = "{}" }
        };

        // Act
        await _repository.AddRangeAsync(ticks);
        await _repository.SaveChangesAsync();

        // Assert
        var count = await _context.PriceTicks.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetRecentAsync_ShouldReturnRecentTicks()
    {
        // Arrange
        var ticks = new List<PriceTick>
        {
            new PriceTick { Id = Guid.NewGuid(), Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = "Binance", DuplicateCheckHash = "hash1", RawData = "{}", CreatedAt = DateTime.UtcNow },
            new PriceTick { Id = Guid.NewGuid(), Ticker = "ETHUSDT", Price = 3000M, Volume = 2.0M, Timestamp = 1640995201, Source = "Binance", DuplicateCheckHash = "hash2", RawData = "{}", CreatedAt = DateTime.UtcNow }
        };

        await _repository.AddRangeAsync(ticks);
        await _repository.SaveChangesAsync();

        // Act
        var recentTicks = await _repository.GetRecentAsync(10);

        // Assert
        Assert.Equal(2, recentTicks.Count);
        Assert.Contains(recentTicks, t => t.Ticker == "BTCUSDT");
        Assert.Contains(recentTicks, t => t.Ticker == "ETHUSDT");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnTick_WhenExists()
    {
        // Arrange
        var tickId = Guid.NewGuid();
        var tick = new PriceTick { Id = tickId, Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = "Binance", DuplicateCheckHash = "hash1", RawData = "{}" };

        await _repository.AddAsync(tick);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(tickId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tickId, result.Id);
        Assert.Equal("BTCUSDT", result.Ticker);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var ticks = new List<PriceTick>
        {
            new PriceTick { Id = Guid.NewGuid(), Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = "Binance", DuplicateCheckHash = "hash1", RawData = "{}" },
            new PriceTick { Id = Guid.NewGuid(), Ticker = "ETHUSDT", Price = 3000M, Volume = 2.0M, Timestamp = 1640995201, Source = "Binance", DuplicateCheckHash = "hash2", RawData = "{}" }
        };

        await _repository.AddRangeAsync(ticks);
        await _repository.SaveChangesAsync();

        // Act
        var count = await _repository.CountAsync();

        // Assert
        Assert.Equal(2, count);
    }
}
