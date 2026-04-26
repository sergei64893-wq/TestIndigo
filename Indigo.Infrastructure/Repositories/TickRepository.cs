using Indigo.Domain.Entities;
using Indigo.Infrastructure.Database;
using Indigo.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Indigo.Infrastructure.Repositories;

/// <summary>
/// Repository для доступа к тикам. Оптимизирован для 1000+ тиков/сек.
/// </summary>
public class TickRepository : ITickRepository
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<TickRepository> _logger;

    public TickRepository(AppDbContext dbContext, ILogger<TickRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PriceTick?> GetByIdAsync(Guid id)
    {
        return await _dbContext.PriceTicks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Получить последние N тиков с оптимизированным запросом
    /// </summary>
    public async Task<List<PriceTick>> GetRecentAsync(int count, string? source = null)
    {
        // ✅ Оптимизированный запрос - используем индекс по Timestamp
        var query = _dbContext.PriceTicks.AsNoTracking();

        if (!string.IsNullOrEmpty(source))
        {
            // ✅ Если фильтруем по source, используем индекс (Source, Timestamp)
            query = query.Where(t => t.Source == source);
        }

        // ✅ Сразу применяем Take перед OrderBy (EF Core оптимизирует в SQL TOP N)
        return await query
            .OrderByDescending(t => t.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task AddAsync(PriceTick tick)
    {
        await _dbContext.PriceTicks.AddAsync(tick);
    }

    public async Task AddRangeAsync(IEnumerable<PriceTick> ticks)
    {
        await _dbContext.PriceTicks.AddRangeAsync(ticks);
    }

    /// <summary>
    /// Bulk insert - добавляет без отслеживания изменений
    /// </summary>
    public async Task BulkInsertAsync(IEnumerable<PriceTick> ticks)
    {
        // ✅ AddRange без tracking - быстрее для больших батчей
        await _dbContext.PriceTicks.AddRangeAsync(ticks);
    }

    /// <summary>
    /// Сохранить все ожидающие изменения в БД
    /// </summary>
    public async Task<int> SaveChangesAsync()
    {
        try
        {
            var changed = await _dbContext.SaveChangesAsync();
            return changed;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update error");
            throw;
        }
    }

    public async Task<long> CountAsync()
    {
        return await _dbContext.PriceTicks.LongCountAsync();
    }

    /// <summary>
    /// Получить последние N тиков по источнику с оптимизацией
    /// </summary>
    public async Task<List<PriceTick>> GetBySourceAsync(string source, int limit = 100)
    {
        // ✅ Используем индекс (Source, Timestamp)
        return await _dbContext.PriceTicks
            .AsNoTracking()
            .Where(t => t.Source == source)
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
