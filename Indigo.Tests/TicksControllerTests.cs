using Indigo.Controllers;
using Indigo.Domain.Entities;
using Indigo.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using Newtonsoft.Json.Linq;

namespace Indigo.Tests;

public class TicksControllerTests
{
    private readonly Mock<ITickRepository> _repositoryMock;
    private readonly TicksController _controller;

    public TicksControllerTests()
    {
        _repositoryMock = new Mock<ITickRepository>();
        _controller = new TicksController(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetRecentTicks_ShouldReturnOkResult_WithTicks()
    {
        // Arrange
        var ticks = new List<PriceTick>
        {
            new PriceTick { Id = Guid.NewGuid(), Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = "Binance", DuplicateCheckHash = "hash1", RawData = "{}" },
            new PriceTick { Id = Guid.NewGuid(), Ticker = "ETHUSDT", Price = 3000M, Volume = 2.0M, Timestamp = 1640995201, Source = "Binance", DuplicateCheckHash = "hash2", RawData = "{}" }
        };

        _repositoryMock.Setup(r => r.GetRecentAsync(100, null)).ReturnsAsync(ticks);

        // Act
        var result = await _controller.GetRecentTicks(100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = JObject.FromObject(okResult.Value);
        Assert.Equal(2, response["count"].Value<int>());
        var returnedTicks = response["ticks"].ToObject<List<PriceTick>>();
        Assert.Equal(ticks.Count, returnedTicks.Count);
    }

    [Fact]
    public async Task GetBySource_ShouldReturnOkResult_WithTicks()
    {
        // Arrange
        var source = "Binance";
        var ticks = new List<PriceTick>
        {
            new PriceTick { Id = Guid.NewGuid(), Ticker = "BTCUSDT", Price = 50000M, Volume = 1.5M, Timestamp = 1640995200, Source = source, DuplicateCheckHash = "hash1", RawData = "{}" }
        };

        _repositoryMock.Setup(r => r.GetBySourceAsync(source, 100)).ReturnsAsync(ticks);

        // Act
        var result = await _controller.GetBySource(source, 100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = JObject.FromObject(okResult.Value);
        Assert.Equal(source, response["source"].Value<string>());
        Assert.Equal(1, response["count"].Value<int>());
        var returnedTicks = response["ticks"].ToObject<List<PriceTick>>();
        Assert.Equal(ticks.Count, returnedTicks.Count);
    }

    [Fact]
    public async Task GetCount_ShouldReturnOkResult_WithCount()
    {
        // Arrange
        var expectedCount = 42L;
        _repositoryMock.Setup(r => r.CountAsync()).ReturnsAsync(expectedCount);

        // Act
        var result = await _controller.GetCount();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = JObject.FromObject(okResult.Value);
        Assert.Equal(expectedCount, response["count"].Value<long>());
    }
}
