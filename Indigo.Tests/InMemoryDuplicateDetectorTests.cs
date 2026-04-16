using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Indigo.Tests;

public class InMemoryDuplicateDetectorTests
{
    private readonly IMemoryCache _cache;
    private readonly InMemoryDuplicateDetector _detector;

    public InMemoryDuplicateDetectorTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _detector = new InMemoryDuplicateDetector(_cache);
    }

    [Fact]
    public async Task IsDuplicateAsync_ShouldReturnFalse_ForNewHash()
    {
        // Arrange
        var hash = "newHash";

        // Act
        var result = await _detector.IsDuplicateAsync(hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsDuplicateAsync_ShouldReturnTrue_ForExistingHash()
    {
        // Arrange
        var hash = "existingHash";
        await _detector.RegisterTickAsync(hash, TimeSpan.FromMinutes(1));

        // Act
        var result = await _detector.IsDuplicateAsync(hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RegisterTickAsync_ShouldRegisterHash()
    {
        // Arrange
        var hash = "testHash";

        // Act
        await _detector.RegisterTickAsync(hash, TimeSpan.FromMinutes(1));

        // Assert
        var isDuplicate = await _detector.IsDuplicateAsync(hash);
        Assert.True(isDuplicate);
    }
}
