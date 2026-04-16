using Indigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Indigo.Infrastructure.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<PriceTick> PriceTicks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PriceTick конфигурация
        modelBuilder.Entity<PriceTick>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Ticker).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DuplicateCheckHash).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Price).HasPrecision(18, 8);
            entity.Property(e => e.Volume).HasPrecision(18, 8);
            
            // Индексы для быстрого поиска
            entity.HasIndex(e => e.Ticker);
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.DuplicateCheckHash);
            entity.HasIndex(e => new { e.Source, e.Timestamp });
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}

