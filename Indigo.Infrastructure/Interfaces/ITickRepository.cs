using Indigo.Domain.Entities;

namespace Indigo.Infrastructure.Interfaces;

/// <summary>
/// Repository для доступа к тикам
/// </summary>
public interface ITickRepository
{
    Task<PriceTick?> GetByIdAsync(Guid id);
    Task<List<PriceTick>> GetRecentAsync(int count, string? source = null);
    Task AddAsync(PriceTick tick);
    Task AddRangeAsync(IEnumerable<PriceTick> ticks);
    Task BulkInsertAsync(IEnumerable<PriceTick> ticks);
    Task<int> SaveChangesAsync();
    Task<long> CountAsync();
    Task<List<PriceTick>> GetBySourceAsync(string source, int limit = 100);
}
