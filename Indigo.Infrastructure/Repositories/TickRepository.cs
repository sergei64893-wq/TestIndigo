using Indigo.Domain.Entities;
using Indigo.Infrastructure.Database;
using Indigo.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Indigo.Infrastructure.Repositories;

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
        return await _dbContext.PriceTicks.FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<PriceTick>> GetRecentAsync(int count, string? source = null)
    {
        var query = _dbContext.PriceTicks.AsNoTracking().AsQueryable();
        
        if (!string.IsNullOrEmpty(source))
        {
            query = query.Where(t => t.Source == source);
        }

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

    public async Task BulkInsertAsync(IEnumerable<PriceTick> ticks)
    {
        // Используем обычный AddRange вместо платного BulkInsertAsync
        await _dbContext.PriceTicks.AddRangeAsync(ticks);
    }

    public async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _dbContext.SaveChangesAsync();
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

    public async Task<List<PriceTick>> GetBySourceAsync(string source, int limit = 100)
    {
        return await _dbContext.PriceTicks
            .AsNoTracking()
            .Where(t => t.Source == source)
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
